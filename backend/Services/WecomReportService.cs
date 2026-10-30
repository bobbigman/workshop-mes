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
            throw ThrowHelper.BizUser("系统没有获取到你的企微用户信息，没法帮你报工。请退出对话重新进入，或联系管理员核对账号对应关系。");

        var orderNo = (dto.OrderNo ?? "").Trim();
        var opName = (dto.OperationName ?? "").Trim();
        if (string.IsNullOrEmpty(orderNo) || string.IsNullOrEmpty(opName))
            throw ThrowHelper.BizUser("请说清楚你要报工的工单号和工序名称，系统猜不出来；请重新完整地告诉机器人。");
        if (dto.GoodQty < 0 || dto.DefectQty < 0)
            throw ThrowHelper.BizUser("良品数和不良品数必须是数字，而且不能是负数，请检查后重新输入正确数量。");
        if (dto.GoodQty == 0 && dto.DefectQty == 0)
            throw ThrowHelper.BizUser("请告诉机器人你要报多少件，不说数量没法报工。");

        // 复杂报工回退 149：有不良/原因不强行对话直录
        var defectReason = (dto.DefectReason ?? "").Trim();
        if (dto.DefectQty > 0 || !string.IsNullOrEmpty(defectReason))
            return ApiResult<WecomReportPreviewResultDto>.Fail(
                "有不良或不良原因，请走报工表单（机器人发卡）填写，对话直录只支持良品报数",
                ComplexNeedFormCode);

        var user = dto.VerifiedLoginUserId != null && dto.McpOwner != null
            ? await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == dto.VerifiedLoginUserId && u.FactoryId == factoryId && u.Status == 1 && u.UserType != UserTypeHelper.External)
            : await ResolveBoundUserAsync(factoryId, dto.ChanType, wecomUserId);
        if (user == null) throw ThrowHelper.BizUser("你的报工身份还没验证过，或者账号对应关系有问题。请先完成身份验证，或联系管理员核对。");

        var order = await _db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.FactoryId == factoryId && o.OrderNo == orderNo);
        if (order == null)
            throw ThrowHelper.BizUser($"你说的工单号 {orderNo} 查不到，可能输入有误或工单不存在，请重新确认工单号再说一遍。");

        var operationId = await ResolveOperationIdAsync(order, opName);
        if ((dto.ExpectedOrderId != null && dto.ExpectedOrderId != order.Id) ||
            (dto.ExpectedOperationId != null && dto.ExpectedOperationId != operationId))
            throw ThrowHelper.BizUser("之前查到的工单或工序信息已经变了，请重新查询后再选择。");
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
            throw ThrowHelper.BizUser("确认信息失效了，请重新预览报工信息后再确认提交。");

        var cacheKey = CacheKeyPrefix + token;
        if (!_cache.TryGetValue(cacheKey, out PendingConfirm? pending) || pending == null)
            throw ThrowHelper.BizUser("确认信息已经过期或失效了，请重新预览报工信息后再确认提交。");
        if (pending.FactoryId != factoryId)
            throw ThrowHelper.BizUser("确认信息和你当前的工厂不一致，请重新预览报工信息后再确认提交。");
        if (pending.McpOwner != dto.McpOwner ||
            (pending.McpOwner != null && pending.UserId != dto.McpUserId))
            throw ThrowHelper.BizUser("确认信息和你当前的账号或对话不匹配，请重新预览报工信息后再确认提交。");

        // 已确认过：幂等回放（同一 token 只落一次）
        if (pending.Used)
        {
            if (pending.CachedResult != null)
                return ApiResult<object?>.Ok(pending.CachedResult);
            throw ThrowHelper.BizUser("这条确认信息已经用过了，不能再用，请重新预览报工信息后再确认提交。");
        }

        if (!ConfirmLocks.TryAdd(token, 0))
        {
            throw ThrowHelper.BizUser("报工正在提交中，请不要重复操作，稍等一下再按原来的步骤重试。");
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
                throw ThrowHelper.BizUser("你不能取消别人的预览操作，只能取消自己的；如需处理他人的请联系管理员。");
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
            throw ThrowHelper.BizUser("这张工单下面没有工序，没法报工，请联系管理员给这张工单添加工序。");

        var ops = await _db.Operations.AsNoTracking()
            .Where(o => o.FactoryId == order.FactoryId && opIds.Contains(o.Id))
            .Select(o => new { o.Id, o.Name })
            .ToListAsync();

        var exact = ops.Where(o => string.Equals(o.Name, operationName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1) return exact[0].Id;
        if (exact.Count > 1)
            throw ThrowHelper.BizUser($"工序「{operationName}」在这张工单里有好几道同名的，系统分不清是哪一道。请到报工表单页面手动选择具体工序。");

        var fuzzy = ops.Where(o => o.Name.Contains(operationName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (fuzzy.Count == 1) return fuzzy[0].Id;
        if (fuzzy.Count > 1)
            throw ThrowHelper.BizUser($"工序「{operationName}」匹配到了好几道工序，系统分不清。请说出完整的工序名称，或到报工表单页面手动选择。");

        throw ThrowHelper.BizUser($"这张工单里没有你说的工序「{operationName}」，请重新确认工序名称，或到报工表单页面查看有哪些工序可选。");
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
