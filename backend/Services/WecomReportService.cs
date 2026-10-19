using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Mcp;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

/// <summary>企微对话报工：预览闸门 + 确认落库（docs/241）。鉴权在 Controller；认人走 WxBinds。</summary>
public interface IWecomReportService
{
    Task<ApiResult<WecomReportPreviewResultDto>> PreviewAsync(WecomReportPreviewDto dto, long factoryId);
    Task<ApiResult<object?>> ConfirmAsync(WecomReportConfirmDto dto, long factoryId);
    void CancelMcpPreview(string token, long factoryId, string owner);
}

public class WecomReportPreviewDto
{
    [JsonIgnore] public string? McpOwner { get; set; }
    [JsonIgnore] public long? VerifiedLoginUserId { get; set; }
    [JsonIgnore] public long? ExpectedOrderId { get; set; }
    [JsonIgnore] public long? ExpectedOperationId { get; set; }
    /// <summary>企微会话 userid（明文；MES 侧按 SHA256 查 WxBinds）</summary>
    public string? UserId { get; set; }
    /// <summary>渠道，默认 wecom</summary>
    public string? ChanType { get; set; }
    public string? OrderNo { get; set; }
    /// <summary>工序名称（本工单内唯一匹配）</summary>
    public string? OperationName { get; set; }
    public int GoodQty { get; set; }
    public int DefectQty { get; set; }
    /// <summary>不良原因名称（可选；有不良/原因时对话直录拒绝，引导 149 表单）</summary>
    public string? DefectReason { get; set; }
}

public class WecomReportConfirmDto
{
    public string? ConfirmToken { get; set; }
    [JsonIgnore] public string? McpOwner { get; set; }
    [JsonIgnore] public long? McpUserId { get; set; }
}

public class WecomReportPreviewResultDto
{
    public string ConfirmToken { get; set; } = "";
    public string PreviewText { get; set; } = "";
    public string OrderNo { get; set; } = "";
    public string OperationName { get; set; } = "";
    public int GoodQty { get; set; }
    public int DefectQty { get; set; }
    public string UserName { get; set; } = "";
    public int ExpireMinutes { get; set; }
}

public class WecomReportService : IWecomReportService
{
    public const string ChanWecom = "wecom";
    public const int ComplexNeedFormCode = 1003;
    private const string CacheKeyPrefix = "wecom-report-confirm:";

    private static readonly ConcurrentDictionary<string, byte> ConfirmLocks = new();

    private readonly AppDbContext _db;
    private readonly IReportService _reportService;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;
    private readonly ILogger<WecomReportService> _logger;

    public WecomReportService(
        AppDbContext db,
        IReportService reportService,
        IMemoryCache cache,
        IConfiguration config,
        ILogger<WecomReportService> logger)
    {
        _db = db;
        _reportService = reportService;
        _cache = cache;
        _config = config;
        _logger = logger;
    }

    public async Task<ApiResult<WecomReportPreviewResultDto>> PreviewAsync(WecomReportPreviewDto dto, long factoryId)
    {
        var wecomUserId = (dto.UserId ?? "").Trim();
        if (string.IsNullOrEmpty(wecomUserId))
            throw ThrowHelper.BizUser("缺少企微 userid，请重新对话");

        var orderNo = (dto.OrderNo ?? "").Trim();
        var opName = (dto.OperationName ?? "").Trim();
        if (string.IsNullOrEmpty(orderNo) || string.IsNullOrEmpty(opName))
            throw ThrowHelper.BizUser("请说清工单号和工序，不要猜不补");
        if (dto.GoodQty < 0 || dto.DefectQty < 0)
            throw ThrowHelper.BizUser("良品/不良数必须是数字且不能为负");
        if (dto.GoodQty == 0 && dto.DefectQty == 0)
            throw ThrowHelper.BizUser("请说明报工数量");

        // 复杂报工回退 149：有不良/原因不强行对话直录
        var defectReason = (dto.DefectReason ?? "").Trim();
        if (dto.DefectQty > 0 || !string.IsNullOrEmpty(defectReason))
            return ApiResult<WecomReportPreviewResultDto>.Fail(
                "有不良或不良原因，请走报工表单（机器人发卡）填写，对话直录只支持良品报数",
                ComplexNeedFormCode);

        var user = dto.VerifiedLoginUserId != null && dto.McpOwner != null
            ? await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == dto.VerifiedLoginUserId && u.FactoryId == factoryId && u.Status == 1 && u.UserType != UserTypeHelper.External)
            : await ResolveBoundUserAsync(factoryId, dto.ChanType, wecomUserId);
        if (user == null) throw ThrowHelper.BizUser("请先验证报工身份或联系管理员核对有效绑定");

        var order = await _db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.FactoryId == factoryId && o.OrderNo == orderNo);
        if (order == null)
            throw ThrowHelper.BizUser($"工单号 {orderNo} 查不到，请重新说清");

        var operationId = await ResolveOperationIdAsync(order, opName);
        if ((dto.ExpectedOrderId != null && dto.ExpectedOrderId != order.Id) ||
            (dto.ExpectedOperationId != null && dto.ExpectedOperationId != operationId))
            throw ThrowHelper.BizUser("候选工单或工序已变化，请重新查询后选择");
        var resolvedOpName = await _db.Operations.AsNoTracking()
            .Where(o => o.Id == operationId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync() ?? opName;

        var reportDto = new ReportDto
        {
            OrderId = order.Id,
            OperationId = operationId,
            GoodQty = dto.GoodQty,
            DefectQty = 0,
            DefectId = null,
            DurationMinutes = 0,
            Source = 3
        };
        await _reportService.ValidateSubmitAsync(reportDto, user.Id);

        var expireMinutes = Math.Clamp(_config.GetValue("WecomReport:ConfirmExpireMinutes", 5), 1, 30);
        var token = Guid.NewGuid().ToString("N");
        var pending = new PendingConfirm
        {
            FactoryId = factoryId,
            McpOwner = dto.McpOwner,
            UserId = user.Id,
            UserName = user.Name,
            OrderId = order.Id,
            OrderNo = order.OrderNo,
            OperationId = operationId,
            OperationName = resolvedOpName,
            GoodQty = dto.GoodQty,
            DefectQty = 0,
            CreatedAt = DateTime.Now
        };
        _cache.Set(CacheKeyPrefix + token, pending, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(expireMinutes)
        });

        var previewText =
            $"{user.Name}，单号{order.OrderNo}/工序{resolvedOpName}/良品{dto.GoodQty}/不良0，对吗？回「确认」后入账。";

        _logger.LogInformation(
            "企微报工预览 factory={FactoryId} userId={UserId} order={OrderNo} op={Op} good={Good} tokenPrefix={Prefix}",
            factoryId, user.Id, order.OrderNo, resolvedOpName, dto.GoodQty, token[..8]);

        return ApiResult<WecomReportPreviewResultDto>.Ok(new WecomReportPreviewResultDto
        {
            ConfirmToken = token,
            PreviewText = previewText,
            OrderNo = order.OrderNo,
            OperationName = resolvedOpName,
            GoodQty = dto.GoodQty,
            DefectQty = 0,
            UserName = user.Name,
            ExpireMinutes = expireMinutes
        });
    }

    public async Task<ApiResult<object?>> ConfirmAsync(WecomReportConfirmDto dto, long factoryId)
    {
        var token = (dto.ConfirmToken ?? "").Trim();
        if (string.IsNullOrEmpty(token) || token.Length > 64)
            throw ThrowHelper.BizUser("确认凭证无效，请重新预览");

        var cacheKey = CacheKeyPrefix + token;
        if (!_cache.TryGetValue(cacheKey, out PendingConfirm? pending) || pending == null)
            throw ThrowHelper.BizUser("确认已过期或无效，请重新报数预览");
        if (pending.FactoryId != factoryId)
            throw ThrowHelper.BizUser("确认凭证与工厂不符，请重新预览");
        if (pending.McpOwner != dto.McpOwner ||
            (pending.McpOwner != null && pending.UserId != dto.McpUserId))
            throw ThrowHelper.BizUser("确认凭证与当前员工或会话不符");

        // 已确认过：幂等回放（同一 token 只落一次）
        if (pending.Used)
        {
            if (pending.CachedResult != null)
                return ApiResult<object?>.Ok(pending.CachedResult);
            throw ThrowHelper.BizUser("确认已使用，请重新报数预览");
        }

        if (!ConfirmLocks.TryAdd(token, 0))
        {
            throw ThrowHelper.BizUser("报工正在提交，请稍后按原凭证重试");
        }

        try
        {
            if (pending.Used && pending.CachedResult != null)
                return ApiResult<object?>.Ok(pending.CachedResult);

            var now = DateTime.Now;
            var reportDto = new ReportDto
            {
                OrderId = pending.OrderId,
                OperationId = pending.OperationId,
                GoodQty = pending.GoodQty,
                DefectQty = pending.DefectQty,
                DefectId = null,
                DurationMinutes = 0,
                ClientRequestId = token,
                Source = 3,
                ConfirmedAt = now
            };

            var result = await _reportService.SubmitAsync(reportDto, pending.UserId);
            if (result.Code != 0) return result;
            pending.Used = true;
            pending.CachedResult = result.Data;
            // 延长一点缓存，便于短时重复确认幂等
            _cache.Set(cacheKey, pending, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            });

            _logger.LogInformation(
                "企微报工确认落库 factory={FactoryId} userId={UserId} order={OrderNo} op={Op} good={Good} tokenPrefix={Prefix}",
                factoryId, pending.UserId, pending.OrderNo, pending.OperationName, pending.GoodQty, token[..Math.Min(8, token.Length)]);

            return result;
        }
        finally
        {
            ConfirmLocks.TryRemove(token, out _);
        }
    }

    public void CancelMcpPreview(string token, long factoryId, string owner)
    {
        if (_cache.TryGetValue(CacheKeyPrefix + token, out PendingConfirm? pending) && pending != null)
        {
            if (pending.FactoryId != factoryId || pending.McpOwner != owner)
                throw ThrowHelper.BizUser("不能取消其他员工或会话的预览");
            _cache.Remove(CacheKeyPrefix + token);
        }
    }

    private async Task<SysUser?> ResolveBoundUserAsync(long factoryId, string? chanType, string rawUserId)
    {
        var chan = NormalizeChan(chanType);
        var openHash = McpWxBindService.HashSession(rawUserId);
        var bind = await _db.WxBinds.AsNoTracking()
            .FirstOrDefaultAsync(b =>
                b.FactoryId == factoryId && b.ChanType == chan && b.WxOpenid == openHash && b.Status == 1);
        if (bind == null) return null;

        return await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == bind.UserId && u.FactoryId == factoryId && u.Status == 1);
    }

    private async Task<long> ResolveOperationIdAsync(ProdWorkOrder order, string operationName)
    {
        var opIds = await _db.WorkOrderOperations.AsNoTracking()
            .Where(t => t.WorkOrderId == order.Id)
            .Select(t => t.OperationId)
            .ToListAsync();
        if (opIds.Count == 0)
        {
            var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == order.ProductId);
            if (product?.RoutingId != null)
            {
                opIds = await _db.RoutingSteps.AsNoTracking()
                    .Where(s => s.RoutingId == product.RoutingId)
                    .Select(s => s.OperationId)
                    .ToListAsync();
            }
        }

        if (opIds.Count == 0)
            throw ThrowHelper.BizUser("该工单没有工序，请联系管理员");

        var ops = await _db.Operations.AsNoTracking()
            .Where(o => o.FactoryId == order.FactoryId && opIds.Contains(o.Id))
            .Select(o => new { o.Id, o.Name })
            .ToListAsync();

        var exact = ops.Where(o => string.Equals(o.Name, operationName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1) return exact[0].Id;
        if (exact.Count > 1)
            throw ThrowHelper.BizUser($"工序「{operationName}」有多条同名，请走报工表单");

        var fuzzy = ops.Where(o => o.Name.Contains(operationName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (fuzzy.Count == 1) return fuzzy[0].Id;
        if (fuzzy.Count > 1)
            throw ThrowHelper.BizUser($"工序「{operationName}」匹配到多道，请说全称或走报工表单");

        throw ThrowHelper.BizUser($"本工单没有工序「{operationName}」，请重新说清");
    }

    private static string NormalizeChan(string? chanType)
    {
        var c = (chanType ?? ChanWecom).Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(c) ? ChanWecom : c;
    }

    private sealed class PendingConfirm
    {
        public string? McpOwner { get; set; }
        public long FactoryId { get; set; }
        public long UserId { get; set; }
        public string UserName { get; set; } = "";
        public long OrderId { get; set; }
        public string OrderNo { get; set; } = "";
        public long OperationId { get; set; }
        public string OperationName { get; set; } = "";
        public int GoodQty { get; set; }
        public int DefectQty { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool Used { get; set; }
        public object? CachedResult { get; set; }
    }
}
