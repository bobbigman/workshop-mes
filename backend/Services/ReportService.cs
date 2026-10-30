using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IReportService
{
    Task<ApiResult<object?>> SubmitAsync(ReportDto dto, long currentUserId);
    /// <summary>报工提交前校验（不落库）。企微对话预览闸门（docs/241）复用同一套部门/超额/前道规则。</summary>
    Task ValidateSubmitAsync(ReportDto dto, long currentUserId);
    Task<ApiResult<object?>> WithdrawAsync(long id, long currentUserId);
    Task<ApiResult<object?>> UpdateAsync(long id, ReportDto dto, long currentUserId);
    Task<ApiResult<PageResult<ReportListDto>>> QueryAsync(ReportQueryDto query, long factoryId, long? userId = null);
    Task<ApiResult<PageResult<ReportListDto>>> QueryMineAsync(ReportMineQueryDto query, long factoryId, long userId);
    Task<ApiResult<ReportTodaySummaryDto>> TodaySummaryAsync(long factoryId, long userId);
    /// <summary>报工提交前工价只读预览（docs/269）：只算额，不写库、不校验、不触发提交/复核链。</summary>
    Task<ApiResult<ReportWagePreviewDto>> PreviewWageForSubmitAsync(ReportWagePreviewInputDto dto, long factoryId, long currentUserId);
    Task<ApiResult<List<ReportChangeLogDto>>> GetChangeLogsAsync(long reportId, long factoryId, long currentUserId);
    Task<ApiResult<List<ReportCandidateDto>>> GetCandidatesAsync(long operationId, long factoryId, long currentUserId);
    Task<ApiResult<List<DefectItemDto>>> GetDefectsByOperationAsync(long operationId);
    Task<ApiResult<BatchReportResultDto>> BatchReportAsync(BatchReportDto dto, long currentUserId);
}

public class ReportDto : WorkTimeInput
{
    public long OrderId { get; set; }
    public long OperationId { get; set; }
    public int GoodQty { get; set; }
    public int DefectQty { get; set; }
    public long? DefectId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool FromPendingSession { get; set; }
    /// <summary>客户端本次提交唯一标识；同一批重试沿用；不传则跳过去重。</summary>
    public string? ClientRequestId { get; set; }
    /// <summary>多人分摊参与人（含自己）；空=单人。兼容旧端；有 Participants 时以 Participants 为准。</summary>
    public List<long>? ParticipantUserIds { get; set; }
    /// <summary>多人分摊参与人含时长（docs/202）；优先于 ParticipantUserIds。</summary>
    public List<ReportShareParticipantDto>? Participants { get; set; }
    /// <summary>分摊方式：1=平均(默认) 2=按报工时长加权（docs/202）。</summary>
    public byte ShareMode { get; set; } = 1;
    /// <summary>代报被代报人列表；空=非代报。与 ParticipantUserIds 互斥。</summary>
    public List<ReportAssigneeDto>? Assignees { get; set; }
    /// <summary>报工自定义字段：键=字段 Id，值=填写内容。空则不写值表。</summary>
    public Dictionary<long, string>? Ext { get; set; }
    /// <summary>
    /// 来源：Update 改数日志用 1=PC 2=H5（docs/205）；
    /// Submit 写入 prod_report.source：0=常规 3=企微对话（docs/241）。
    /// </summary>
    public byte Source { get; set; } = 1;
    /// <summary>企微对话确认时间（docs/241）；仅 Source=3 时写入 prod_report.confirmed_at。</summary>
    public DateTime? ConfirmedAt { get; set; }
    /// <summary>软提示确认后重提：true=跳过 soft 拦截（hard 仍拦）。docs/150。</summary>
    public bool ConfirmUpstream { get; set; }
}

/// <summary>报工提交前工价只读预览入参（docs/269）。</summary>
public class ReportWagePreviewInputDto
{
    public long OrderId { get; set; }
    public long OperationId { get; set; }
    public int GoodQty { get; set; }
    public int DefectQty { get; set; }
    public int DurationMinutes { get; set; }
}

/// <summary>报工提交前工价只读预览出参（docs/269）。</summary>
public class ReportWagePreviewDto
{
    public bool Matched { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? WageAmount { get; set; }
    public byte PriceType { get; set; }
}

public class ReportAssigneeDto : WorkTimeInput
{
    public long UserId { get; set; }
    public int GoodQty { get; set; }
    public int DefectQty { get; set; }
    public long? DefectId { get; set; }
}

public class ReportShareParticipantDto : WorkTimeInput
{
    public long UserId { get; set; }
}

public class ReportSubmitItemDto
{
    public int DurationMinutes { get; set; }
    public DateTime? StartTime { get; set; }
    public byte PriceType { get; set; }
    public long Id { get; set; }
    public long UserId { get; set; }
    public string UserName { get; set; } = "";
    public long OperationId { get; set; }
    public string OperationName { get; set; } = "";
    public int GoodQty { get; set; }
    public int DefectQty { get; set; }
    public byte ReviewStatus { get; set; }
    public DateTime ReportTime { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? WageAmount { get; set; }
}

public class ReportSubmitResultDto
{
    public List<ReportSubmitItemDto> Reports { get; set; } = new();
    public string? ShareBatchNo { get; set; }
    public bool IdempotentReplay { get; set; }
}

public class ReportCandidateDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public byte Role { get; set; }
}

public class ReportListDto
{
    public long Id { get; set; }
    public long OperationId { get; set; }
    public string OrderNo { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string OperationName { get; set; } = "";
    public string UserName { get; set; } = "";
    /// <summary>报工人人员类型（docs/161）</summary>
    public byte UserType { get; set; }
    public int GoodQty { get; set; }
    public int DefectQty { get; set; }
    public long? DefectId { get; set; }
    public string? DefectName { get; set; }
    public int DurationMinutes { get; set; }
    public byte ReviewStatus { get; set; }
    public string? RejectReason { get; set; }
    public DateTime ReportTime { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? WageAmount { get; set; }
    public bool SettledFlag { get; set; }
    public Dictionary<long, string> Ext { get; set; } = new();
}

/// <summary>本人今日计件汇总（docs/206）：已通过+待复核，不含已退回。</summary>
public class ReportTodaySummaryDto
{
    public int TodayGoodQty { get; set; }
    public decimal TodayWage { get; set; }
}

/// <summary>报工修改日志行（docs/205）</summary>
public class ReportChangeLogDto
{
    public long Id { get; set; }
    public long ReportId { get; set; }
    public long ChangedBy { get; set; }
    public string ChangedByName { get; set; } = "";
    public int OldGoodQty { get; set; }
    public int OldDefectQty { get; set; }
    public long? OldDefectId { get; set; }
    public int OldDurationMinutes { get; set; }
    public int NewGoodQty { get; set; }
    public int NewDefectQty { get; set; }
    public long? NewDefectId { get; set; }
    public int NewDurationMinutes { get; set; }
    public byte Source { get; set; }
    public DateTime ChangedAt { get; set; }
}

public class ReportQueryDto
{
    public string? OrderNo { get; set; }
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public byte? ReviewStatus { get; set; }
    /// <summary>按报工人人员类型筛选（docs/161）；不传=全部</summary>
    public byte? UserType { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class ReportMineQueryDto
{
    public DateTime? Date { get; set; }
    public string? OrderNo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>批量补报（docs/17）：管理员一次补齐未报满工序。</summary>
public class BatchReportDto : WorkTimeInput
{
    public long OrderId { get; set; }
    public int GoodQty { get; set; }
    public string? BatchNo { get; set; }
    public List<BatchReportOperationDto>? Allocations { get; set; }
    /// <summary>软提示确认后重提：true=跳过 soft 拦截（hard 仍拦）。docs/150。</summary>
    public bool ConfirmUpstream { get; set; }
}

public class BatchReportOperationDto
{
    public long OperationId { get; set; }
    public List<BatchReportUserDto> Users { get; set; } = new();
}

public class BatchReportUserDto : WorkTimeInput
{
    public long UserId { get; set; }
    public int GoodQty { get; set; }
}

public class BatchReportResultDto
{
    public int OperationCount { get; set; }
    public int ReportCount { get; set; }
}

public class ReportService : IReportService
{
    /// <summary>上级工序未达上限、需前端确认后重提（soft）。</summary>
    public const int UpstreamSoftConfirmCode = 1002;

    private readonly AppDbContext _db;
    private readonly WechatEventService? _events;
    private readonly IReportUpstreamSettingService _upstreamSetting;
    private readonly IReportQtySettingService? _qtySetting;
    public ReportService(
        AppDbContext db,
        IReportUpstreamSettingService upstreamSetting,
        WechatEventService? events = null,
        IReportQtySettingService? qtySetting = null)
    {
        _db = db;
        _upstreamSetting = upstreamSetting;
        _events = events;
        _qtySetting = qtySetting;
    }

    private async Task<ReportQtySettingDto> QtyCfgAsync(long factoryId) =>
        _qtySetting == null ? new ReportQtySettingDto() : await _qtySetting.GetOrDefaultAsync(factoryId);

    private static void EnsureWithinPlan(int alreadyGood, int addGood, int planQty, int ratio, string alreadyLabel, string where)
    {
        var allowed = ReportQtySettingService.AllowedQty(planQty, ratio);
        if (alreadyGood + addGood <= allowed) return;
        if (ratio > 0)
            throw ThrowHelper.Biz(where,
                $"这次报的数量超过了允许的超报上限，报不进去了，请找管理员确认还能报多少。");
        throw ThrowHelper.Biz(where,
            $"这次报的数量已经超过计划数了，报不进去了，请找管理员确认是否还能加报。");
    }

    public async Task<ApiResult<object?>> SubmitAsync(ReportDto dto, long currentUserId)
    {
        var clientReqId = NormalizeClientRequestId(dto.ClientRequestId);
        var hasParticipants = (dto.Participants != null && dto.Participants.Count > 0)
            || (dto.ParticipantUserIds != null && dto.ParticipantUserIds.Count > 0);
        var hasAssignees = dto.Assignees != null && dto.Assignees.Count > 0;
        if (hasParticipants && hasAssignees)
            throw ThrowHelper.Biz(nameof(SubmitAsync), "分摊报工和代报工不能在同一次提交中同时使用，请分开操作。");

        if (clientReqId != null)
        {
            var actor = await _db.Users.AsNoTracking().FirstAsync(x => x.Id == currentUserId);
            var replay = await FindByClientRequestAsync(actor.FactoryId, currentUserId, clientReqId);
            if (replay.Count > 0)
            {
                if (!MatchesSubmitContent(replay, dto, currentUserId, hasParticipants, hasAssignees))
                    throw ThrowHelper.BizUser("同一次提交的内容已被修改过，请使用新的提交标识重新提交，避免重复或脏数据。");
                return ApiResult<object?>.Ok(await BuildSubmitResultAsync(replay, idempotent: true));
            }
        }
        var prepared = await PrepareSubmitAsync(dto, currentUserId, hasParticipants, hasAssignees, nameof(SubmitAsync));

        var order = prepared.Order;
        var lines = prepared.Lines;
        var totalGood = lines.Sum(x => x.GoodQty);
        var now = prepared.Now;
        var reportSource = NormalizeReportSource(dto.Source);
        var confirmedAt = reportSource == 3 ? (dto.ConfirmedAt ?? now) : (DateTime?)null;

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            await OperationPriceListService.LockFactoryAsync(_db, order.FactoryId);
            // 锁工单行，防并发超额
            await WechatEventService.LockOrderAsync(_db, order);
            var beforeStatus = order.Status;

            if (clientReqId != null)
            {
                var raced = await FindByClientRequestAsync(order.FactoryId, currentUserId, clientReqId);
                if (raced.Count > 0)
                {
                    if (!MatchesSubmitContent(raced, dto, currentUserId, hasParticipants, hasAssignees))
                        throw ThrowHelper.Biz(nameof(SubmitAsync), "同一请求标识内容已变更，请换新标识重新提交");
                    await tx.CommitAsync();
                    return ApiResult<object?>.Ok(await BuildSubmitResultAsync(raced, idempotent: true));
                }
            }

            if (order.Status == 4) throw ThrowHelper.BizUser("这张工单还是草稿状态，还不能报工。请让管理员在电脑端把工单提交后再来扫码报工。");
            if (order.Status >= 2) throw ThrowHelper.BizUser("这张工单已经做完或作废了，不能再报工。如果有误，找班组长或管理员处理。");
            var task = await _db.WorkOrderOperations
                .FirstOrDefaultAsync(t => t.WorkOrderId == order.Id && t.OperationId == dto.OperationId);
            var planQty = task?.PlanQty ?? order.Qty;
            var alreadyGood = await _db.Reports
                .Where(r => r.OrderId == order.Id && r.OperationId == dto.OperationId && r.ReviewStatus != 2)
                .SumAsync(r => (int?)r.GoodQty) ?? 0;
            var ratio = (await QtyCfgAsync(order.FactoryId)).OverrunRatio;
            EnsureWithinPlan(alreadyGood, totalGood, planQty, ratio, "已报良品", nameof(SubmitAsync));

            var clock = new WorkSessionService(_db);
            var times = new Dictionary<long, ResolvedWorkTime>();
            foreach (var line in lines)
            {
                WorkTimeInput input = dto.Assignees?.FirstOrDefault(x => x.UserId == line.UserId)
                    as WorkTimeInput ?? dto.Participants?.FirstOrDefault(x => x.UserId == line.UserId)
                    as WorkTimeInput ?? (line.UserId == currentUserId ? dto : new WorkTimeInput());
                times[line.UserId] = await clock.ResolveAsync(order, dto.OperationId, line.UserId, currentUserId, input, now, dto.FromPendingSession);
            }
            if (hasParticipants && dto.ShareMode == 2)
            {
                var parts = lines.Select(x => (x.UserId, DurationMinutes: times[x.UserId].Minutes)).ToList();
                var (goods, defects) = SplitByDuration(dto.GoodQty, dto.DefectQty, parts, currentUserId);
                lines = lines.Select((x, i) => (x.UserId, goods[i], defects[i], defects[i] > 0 ? dto.DefectId : null, times[x.UserId].Minutes)).ToList();
            }
            var created = new List<ProdReport>();
            foreach (var line in lines)
            {
                var deptId = await ResolveDeptSnapshotAsync(line.UserId, dto.OperationId);
                var (_, unitPrice, wageAmount, priceSnapshot) = await PreviewWageAsync(
                    order.FactoryId, order.ProductId, dto.OperationId, line.UserId,
                    line.GoodQty, line.DefectQty, times[line.UserId].Minutes, order.Id, times[line.UserId].ReportTime);

                var report = new ProdReport
                {
                    FactoryId = order.FactoryId,
                    OrderId = order.Id,
                    OperationId = dto.OperationId,
                    UserId = line.UserId,
                    GoodQty = line.GoodQty,
                    DefectQty = line.DefectQty,
                    DefectId = line.DefectId,
                    DurationMinutes = times[line.UserId].Minutes,
                    ProductId = order.ProductId,
                    DepartmentId = deptId,
                    PriceSnapshot = priceSnapshot,
                    UnitPrice = unitPrice,
                    WageAmount = wageAmount,
                    SettledFlag = false,
                    ReviewStatus = prepared.ReviewStatus,
                    ReviewedBy = prepared.ReviewedBy,
                    ReviewedAt = prepared.ReviewedAt,
                    ReportTime = times[line.UserId].ReportTime,
                    ClientRequestId = clientReqId,
                    ShareBatchNo = prepared.ShareBatchNo,
                    OperatorUserId = prepared.OperatorUserId,
                    Source = reportSource,
                    ConfirmedAt = confirmedAt
                };
                _db.Reports.Add(report);
                created.Add(report);
            }

            await _db.SaveChangesAsync();

            foreach (var report in created) await clock.CompleteAsync(times[report.UserId], report, currentUserId);
            await _db.SaveChangesAsync();

            // 扩展字段只写在「提交人自己的那条」或代报第一条
            var extTarget = created.FirstOrDefault(r => r.UserId == currentUserId) ?? created[0];
            await SaveReportExtAsync(extTarget.Id, dto.Ext);

            await RecalcStatusAsync(order);
            if (_events != null) await _events.EnqueueAsync(order, beforeStatus, created);
            await tx.CommitAsync();

            return ApiResult<object?>.Ok(await BuildSubmitResultAsync(created, idempotent: false));
        }
        catch (DbUpdateException ex) when (clientReqId != null && IsUniqueViolation(ex))
        {
            try { await tx.RollbackAsync(); } catch { /* 唯一冲突后事务已失败，忽略二次回滚 */ }
            var existing = await FindByClientRequestAsync(order.FactoryId, currentUserId, clientReqId);
            if (existing.Count > 0 && MatchesSubmitContent(existing, dto, currentUserId, hasParticipants, hasAssignees))
                return ApiResult<object?>.Ok(await BuildSubmitResultAsync(existing, idempotent: true));
            throw ThrowHelper.General(nameof(SubmitAsync), "报工保存冲突，请查我的报工或按原请求重试", ex);
        }
        catch
        {
            try { await tx.RollbackAsync(); } catch { /* 已失败则忽略 */ }
            throw;
        }
    }

    /// <summary>docs/241：预览闸门只跑校验，不写库；确认时仍走 SubmitAsync。</summary>
    public async Task ValidateSubmitAsync(ReportDto dto, long currentUserId)
    {
        var hasParticipants = (dto.Participants != null && dto.Participants.Count > 0)
            || (dto.ParticipantUserIds != null && dto.ParticipantUserIds.Count > 0);
        var hasAssignees = dto.Assignees != null && dto.Assignees.Count > 0;
        if (hasParticipants || hasAssignees)
            throw ThrowHelper.Biz(nameof(ValidateSubmitAsync), "企微对话报工只支持一个人自己报自己的；多人或复杂报工请到报工表单页面操作。");

        var prepared = await PrepareSubmitAsync(dto, currentUserId, hasParticipants: false, hasAssignees: false, nameof(ValidateSubmitAsync));
        await new WorkSessionService(_db).ResolveAsync(prepared.Order, dto.OperationId, currentUserId, currentUserId, dto, prepared.Now);
        var totalGood = prepared.Lines.Sum(x => x.GoodQty);

        var task = await _db.WorkOrderOperations.AsNoTracking()
            .FirstOrDefaultAsync(t => t.WorkOrderId == prepared.Order.Id && t.OperationId == dto.OperationId);
        var planQty = task?.PlanQty ?? prepared.Order.Qty;
        var alreadyGood = await _db.Reports.AsNoTracking()
            .Where(r => r.OrderId == prepared.Order.Id && r.OperationId == dto.OperationId && r.ReviewStatus != 2)
            .SumAsync(r => (int?)r.GoodQty) ?? 0;
        var ratio = (await QtyCfgAsync(prepared.Order.FactoryId)).OverrunRatio;
        EnsureWithinPlan(alreadyGood, totalGood, planQty, ratio, "已报良品", nameof(ValidateSubmitAsync));
    }

    private async Task<PreparedSubmit> PrepareSubmitAsync(
        ReportDto dto,
        long currentUserId,
        bool hasParticipants,
        bool hasAssignees,
        string where)
    {
        var order = await _db.WorkOrders.FirstOrDefaultAsync(o => o.Id == dto.OrderId)
            ?? throw ThrowHelper.Biz(where, "工单不存在");

        var user = await _db.Users.FindAsync(currentUserId)
            ?? throw ThrowHelper.Biz(where, "当前用户不存在");
        if (user.FactoryId != order.FactoryId)
            throw ThrowHelper.Biz(where, "你不能操作别的厂的工单，只能做自己厂的，请确认扫的二维码是本厂工单。");

        if (order.Status == 4)
            throw ThrowHelper.Biz(where, "工单为草稿，请先在电脑端提交后再报工");
        if (order.Status == 2)
            throw ThrowHelper.Biz(where, "这张工单已经结束了，不能再报工。有疑问找班组长或管理员确认。");
        if (order.Status == 3)
            throw ThrowHelper.Biz(where, "这张工单已经作废了，不能再报工。有疑问找班组长或管理员确认。");

        var taskExists = await _db.WorkOrderOperations.AnyAsync(t => t.WorkOrderId == order.Id && t.OperationId == dto.OperationId);
        if (taskExists == false)
        {
            var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == order.ProductId);
            var inRoute = product?.RoutingId != null && await _db.RoutingSteps.AnyAsync(s => s.RoutingId == product.RoutingId && s.OperationId == dto.OperationId);
            if (!inRoute)
                throw ThrowHelper.Biz(where, "该工序不属于此工单");
        }

        List<(long UserId, int GoodQty, int DefectQty, long? DefectId, int DurationMinutes)> lines;
        long? operatorUserId;
        string? shareBatchNo = null;
        byte reviewStatus;
        long? reviewedBy;
        DateTime? reviewedAt;
        var now = DateTime.Now;

        if (hasAssignees)
        {
            if (user.Role != 1 && user.Role != 3)
                throw ThrowHelper.Biz(where, "你没有代别人报工的权限，只有管理员或班组长才能代报。请让有权限的人来操作，或让对方自己报工。");
            lines = await BuildAssigneeLinesAsync(dto, order.FactoryId, currentUserId, user.Role);
            operatorUserId = currentUserId;
            shareBatchNo = NewShareBatchNo();
            reviewStatus = user.Role == 1 ? (byte)1 : (byte)0;
            reviewedBy = user.Role == 1 ? currentUserId : null;
            reviewedAt = user.Role == 1 ? now : null;
        }
        else if (hasParticipants)
        {
            if (!await ReportPermissionHelper.CanReportOperationAsync(_db, currentUserId, user.Role, dto.OperationId))
                throw ThrowHelper.Biz(where, "你所在的部门没有这道工序的报工权限，不能报工。请联系管理员把你所在部门加到这道工序的报工权限里。");
            ValidateQty(dto.GoodQty, dto.DefectQty, dto.DefectId, (dto.DurationMinutes ?? 0), where);
            lines = await BuildShareLinesAsync(dto, currentUserId);
            operatorUserId = currentUserId;
            shareBatchNo = NewShareBatchNo();
            reviewStatus = user.Role == 1 ? (byte)1 : (byte)0;
            reviewedBy = user.Role == 1 ? currentUserId : null;
            reviewedAt = user.Role == 1 ? now : null;
        }
        else
        {
            if (!await ReportPermissionHelper.CanReportOperationAsync(_db, currentUserId, user.Role, dto.OperationId))
                throw ThrowHelper.Biz(where, "无报工权限：您不在该工序的报工权限部门内");
            ValidateQty(dto.GoodQty, dto.DefectQty, dto.DefectId, (dto.DurationMinutes ?? 0), where);
            lines = new List<(long, int, int, long?, int)>
            {
                (currentUserId, dto.GoodQty, dto.DefectQty, dto.DefectId, (dto.DurationMinutes ?? 0))
            };
            operatorUserId = null;
            reviewStatus = user.Role == 1 ? (byte)1 : (byte)0;
            reviewedBy = user.Role == 1 ? currentUserId : null;
            reviewedAt = user.Role == 1 ? now : null;
        }

        var totalGood = lines.Sum(x => x.GoodQty);
        await EnsureUpstreamOkAsync(order, dto.OperationId, totalGood, dto.ConfirmUpstream, where);

        return new PreparedSubmit(order, lines, operatorUserId, shareBatchNo, reviewStatus, reviewedBy, reviewedAt, now);
    }

    /// <summary>prod_report.source：仅认 0/3；其它按 0（常规）。改数日志仍用 dto.Source 的 1/2。</summary>
    private static byte NormalizeReportSource(byte source) => source == 3 ? (byte)3 : (byte)0;

    private sealed record PreparedSubmit(
        ProdWorkOrder Order,
        List<(long UserId, int GoodQty, int DefectQty, long? DefectId, int DurationMinutes)> Lines,
        long? OperatorUserId,
        string? ShareBatchNo,
        byte ReviewStatus,
        long? ReviewedBy,
        DateTime? ReviewedAt,
        DateTime Now);

    public async Task<ApiResult<PageResult<ReportListDto>>> QueryMineAsync(ReportMineQueryDto query, long factoryId, long userId)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 20 : Math.Min(query.PageSize, 100);
        var day = (query.Date ?? DateTime.Today).Date;
        var next = day.AddDays(1);
        var orderNo = string.IsNullOrWhiteSpace(query.OrderNo) ? null : query.OrderNo.Trim();

        var q = from r in _db.Reports.AsNoTracking()
                join o in _db.WorkOrders.AsNoTracking() on r.OrderId equals o.Id
                join p in _db.Products.AsNoTracking() on o.ProductId equals p.Id
                join op in _db.Operations.AsNoTracking() on r.OperationId equals op.Id
                where r.FactoryId == factoryId && r.UserId == userId
                      && r.ReportTime >= day && r.ReportTime < next
                select new { r, o, p, op };

        if (orderNo != null)
            q = q.Where(x => x.o.OrderNo.Contains(orderNo));

        var total = await q.CountAsync();
        var list = await q.OrderByDescending(x => x.r.ReportTime)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new ReportListDto
            {
                Id = x.r.Id,
                OperationId = x.r.OperationId,
                OrderNo = x.o.OrderNo,
                ProductName = x.p.Name,
                OperationName = x.op.Name,
                UserName = "",
                GoodQty = x.r.GoodQty,
                DefectQty = x.r.DefectQty,
                DefectId = x.r.DefectId,
                DurationMinutes = x.r.DurationMinutes,
                ReviewStatus = x.r.ReviewStatus,
                RejectReason = x.r.RejectReason,
                ReportTime = x.r.ReportTime,
                UnitPrice = x.r.UnitPrice,
                WageAmount = x.r.WageAmount,
                SettledFlag = x.r.SettledFlag
            }).ToListAsync();

        return ApiResult<PageResult<ReportListDto>>.Ok(new PageResult<ReportListDto> { List = list, Total = total });
    }

    /// <summary>本人今日计件汇总（docs/206）：review_status IN (0,1)，按 report_time 当日。</summary>
    public async Task<ApiResult<ReportTodaySummaryDto>> TodaySummaryAsync(long factoryId, long userId)
    {
        var day = DateTime.Today;
        var next = day.AddDays(1);
        var rows = await _db.Reports.AsNoTracking()
            .Where(r => r.FactoryId == factoryId && r.UserId == userId
                        && r.ReportTime >= day && r.ReportTime < next
                        && (r.ReviewStatus == 0 || r.ReviewStatus == 1))
            .Select(r => new { r.GoodQty, r.WageAmount })
            .ToListAsync();
        var good = rows.Sum(x => x.GoodQty);
        var wage = rows.Sum(x => x.WageAmount ?? 0m);
        return ApiResult<ReportTodaySummaryDto>.Ok(new ReportTodaySummaryDto
        {
            TodayGoodQty = good,
            TodayWage = Math.Round(wage, 2, MidpointRounding.AwayFromZero)
        });
    }

    /// <summary>报工修改日志（docs/205）：管理员任意；非管理员仅本人报工。</summary>
    public async Task<ApiResult<List<ReportChangeLogDto>>> GetChangeLogsAsync(long reportId, long factoryId, long currentUserId)
    {
        var report = await _db.Reports.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == reportId && r.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(GetChangeLogsAsync), "这条报工记录找不到了，可能已被删除。请刷新页面看最新列表，或联系管理员确认。");

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId)
            ?? throw ThrowHelper.Biz(nameof(GetChangeLogsAsync), "当前用户不存在");
        if (user.Role != 1 && report.UserId != currentUserId)
            throw ThrowHelper.Biz(nameof(GetChangeLogsAsync), "你没有权限查看别人的报工修改记录，只能查看自己的；如需查看他人的请联系管理员。");

        var list = await (from log in _db.ReportChangeLogs.AsNoTracking()
                          where log.ReportId == reportId && log.FactoryId == factoryId
                          join u in _db.Users.AsNoTracking() on log.ChangedBy equals u.Id into ug
                          from u in ug.DefaultIfEmpty()
                          orderby log.ChangedAt descending
                          select new ReportChangeLogDto
                          {
                              Id = log.Id,
                              ReportId = log.ReportId,
                              ChangedBy = log.ChangedBy,
                              ChangedByName = u == null ? "" : u.Name,
                              OldGoodQty = log.OldGoodQty,
                              OldDefectQty = log.OldDefectQty,
                              OldDefectId = log.OldDefectId,
                              OldDurationMinutes = log.OldDurationMinutes,
                              NewGoodQty = log.NewGoodQty,
                              NewDefectQty = log.NewDefectQty,
                              NewDefectId = log.NewDefectId,
                              NewDurationMinutes = log.NewDurationMinutes,
                              Source = log.Source,
                              ChangedAt = log.ChangedAt
                          }).ToListAsync();
        return ApiResult<List<ReportChangeLogDto>>.Ok(list);
    }

    public async Task<ApiResult<List<ReportCandidateDto>>> GetCandidatesAsync(long operationId, long factoryId, long currentUserId)
    {
        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId)
            ?? throw ThrowHelper.Biz(nameof(GetCandidatesAsync), "当前用户不存在");

        var deptIds = await _db.OperationDepartments.AsNoTracking()
            .Where(x => x.OperationId == operationId)
            .Select(x => x.DepartmentId)
            .ToListAsync();
        if (deptIds.Count == 0)
            return ApiResult<List<ReportCandidateDto>>.Ok(new List<ReportCandidateDto>());

        var userIds = await _db.DepartmentUsers.AsNoTracking()
            .Where(x => deptIds.Contains(x.DepartmentId))
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync();

        // 班组长代报选人再限本组
        if (me.Role == 3)
        {
            var myDepts = await _db.DepartmentUsers.AsNoTracking()
                .Where(x => x.UserId == currentUserId)
                .Select(x => x.DepartmentId)
                .ToListAsync();
            var groupUserIds = await _db.DepartmentUsers.AsNoTracking()
                .Where(x => myDepts.Contains(x.DepartmentId))
                .Select(x => x.UserId)
                .Distinct()
                .ToListAsync();
            userIds = userIds.Intersect(groupUserIds).ToList();
        }

        var list = await _db.Users.AsNoTracking()
            .Where(u => u.FactoryId == factoryId && u.Status == 1 && userIds.Contains(u.Id))
            .OrderBy(u => u.Name)
            .Select(u => new ReportCandidateDto { Id = u.Id, Name = u.Name, Role = u.Role })
            .ToListAsync();
        return ApiResult<List<ReportCandidateDto>>.Ok(list);
    }

    private static void ValidateQty(int goodQty, int defectQty, long? defectId, int durationMinutes, string loc)
    {
        if (goodQty < 0 || defectQty < 0)
            throw ThrowHelper.Biz(loc, "良品数和不良品数都不能填负数，请检查一下填的数字。");
        if (defectQty > 0 && defectId == null)
            throw ThrowHelper.Biz(loc, "有不良品的话必须选一个不良原因，不能空着，请从下拉列表里选一个原因再提交。");
        if (durationMinutes < 0)
            throw ThrowHelper.Biz(loc, "报工时长不能填负数，请检查输入的时长，改为正数后再提交。");
        if (durationMinutes > 1440)
            throw ThrowHelper.Biz(loc, "单次报工时长不能超过24小时，请检查填写的时长是否合理；可分开多次报工，或联系管理员确认。");
    }

    private static string? NormalizeClientRequestId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (s.Length > 64)
            throw ThrowHelper.Biz(nameof(SubmitAsync), "请求标识过长，请缩短后重试。");
        return s;
    }

    private static string NewShareBatchNo() =>
        "S" + DateTime.Now.ToString("yyyyMMddHHmmss") + Guid.NewGuid().ToString("N")[..8];

    private async Task<List<ProdReport>> FindByClientRequestAsync(long factoryId, long submitterId, string clientReqId)
    {
        return await _db.Reports
            .Where(r => r.FactoryId == factoryId
                        && r.ClientRequestId == clientReqId
                        && (r.OperatorUserId == submitterId
                            || (r.OperatorUserId == null && r.UserId == submitterId)))
            .OrderBy(r => r.Id)
            .ToListAsync();
    }

    private static bool MatchesSubmitContent(
        List<ProdReport> existing, ReportDto dto, long currentUserId, bool hasParticipants, bool hasAssignees)
    {
        if (existing.Count == 0) return false;
        if (existing.Any(r => r.OrderId != dto.OrderId || r.OperationId != dto.OperationId))
            return false;

        if (hasAssignees)
        {
            var want = (dto.Assignees ?? new()).OrderBy(x => x.UserId).ToList();
            var got = existing.OrderBy(x => x.UserId).ToList();
            return want.Count == got.Count && want.Zip(got).All(pair =>
                pair.First.UserId == pair.Second.UserId && pair.First.GoodQty == pair.Second.GoodQty
                && pair.First.DefectQty == pair.Second.DefectQty && pair.First.DefectId == pair.Second.DefectId
                && (!pair.First.DurationMinutes.HasValue || pair.First.DurationMinutes.Value == pair.Second.DurationMinutes));
        }

        if (hasParticipants)
        {
            var ids = ResolveShareParticipants(dto, currentUserId)
                .Select(p => p.UserId).Distinct().OrderBy(x => x).ToList();
            var gotIds = existing.Select(r => r.UserId).OrderBy(x => x).ToList();
            if (!ids.SequenceEqual(gotIds)) return false;
            return existing.Sum(r => r.GoodQty) == dto.GoodQty
                   && existing.Sum(r => r.DefectQty) == dto.DefectQty;
        }

        // 单人
        if (existing.Count != 1) return false;
        var r0 = existing[0];
        return r0.UserId == currentUserId
               && r0.GoodQty == dto.GoodQty
               && r0.DefectQty == dto.DefectQty
               && r0.DefectId == dto.DefectId
               && (!dto.DurationMinutes.HasValue || r0.DurationMinutes == dto.DurationMinutes.Value);
    }

    private async Task<object> BuildSubmitResultAsync(List<ProdReport> reports, bool idempotent)
    {
        var userIds = reports.Select(r => r.UserId).Distinct().ToList();
        var opIds = reports.Select(r => r.OperationId).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name);
        var reportIds = reports.Select(x => x.Id).ToList();
        var sessions = await _db.WorkSessions.AsNoTracking().Where(x => x.ReportId.HasValue && reportIds.Contains(x.ReportId.Value))
            .ToDictionaryAsync(x => x.ReportId!.Value);
        var receiptTypes = new Dictionary<long, byte>();
        foreach (var report in reports)
        {
            var order = await _db.WorkOrders.AsNoTracking().FirstAsync(x => x.Id == report.OrderId);
            receiptTypes[report.Id] = await new WorkSessionService(_db).PriceTypeAsync(order, report.OperationId, report.UserId, report.ReportTime);
        }
        var opNames = await _db.Operations.AsNoTracking()
            .Where(o => opIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Name);

        return new ReportSubmitResultDto
        {
            IdempotentReplay = idempotent,
            ShareBatchNo = reports.Select(r => r.ShareBatchNo).FirstOrDefault(x => x != null),
            Reports = reports.Select(r => new ReportSubmitItemDto
            {
                Id = r.Id,
                DurationMinutes = r.DurationMinutes,
                StartTime = sessions.GetValueOrDefault(r.Id)?.StartTime,
                PriceType = receiptTypes[r.Id],
                UserId = r.UserId,
                UserName = names.GetValueOrDefault(r.UserId, ""),
                OperationId = r.OperationId,
                OperationName = opNames.GetValueOrDefault(r.OperationId, ""),
                GoodQty = r.GoodQty,
                DefectQty = r.DefectQty,
                ReviewStatus = r.ReviewStatus,
                ReportTime = r.ReportTime,
                UnitPrice = r.UnitPrice,
                WageAmount = r.WageAmount
            }).ToList()
        };
    }

    private async Task<List<(long UserId, int GoodQty, int DefectQty, long? DefectId, int DurationMinutes)>> BuildShareLinesAsync(
        ReportDto dto, long currentUserId)
    {
        var parts = ResolveShareParticipants(dto, currentUserId);
        if (parts.Count < 1)
            throw ThrowHelper.Biz(nameof(SubmitAsync), "分摊报工至少要选一个人，不能空着，请勾选后再提交。");

        foreach (var p in parts)
        {
            var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == p.UserId)
                ?? throw ThrowHelper.Biz(nameof(SubmitAsync), $"参与人在系统中找不到（{p.UserId}），请检查人员列表并移除无效人员后重试。");
            if (u.Status != 1)
                throw ThrowHelper.Biz(nameof(SubmitAsync), $"参与人 {u.Name} 已被停用，无法参与报工；请到人员管理中确认该员工状态，或更换为有效人员。");
            if (!await ReportPermissionHelper.CanReportOperationAsync(_db, p.UserId, u.Role, dto.OperationId))
                throw ThrowHelper.Biz(nameof(SubmitAsync), $"这个人没有这道工序的报工权限，不能帮他报工。请联系管理员给他所在部门加上报工权限：{u.Name}");
            if (p.DurationMinutes < 0)
                throw ThrowHelper.Biz(nameof(SubmitAsync), "报工时长不能为负");
            if (p.DurationMinutes > 1440)
                throw ThrowHelper.Biz(nameof(SubmitAsync), "单次报工时长不能超过24小时");
        }

        var n = parts.Count;
        var shareMode = dto.ShareMode == 2 ? (byte)2 : (byte)1;
        int[] goodParts;
        int[] defectParts;
        if (shareMode == 2)
            (goodParts, defectParts) = SplitByDuration(dto.GoodQty, dto.DefectQty, parts, currentUserId);
        else
            (goodParts, defectParts) = SplitEqual(dto.GoodQty, dto.DefectQty, parts, currentUserId);

        var lines = new List<(long, int, int, long?, int)>();
        for (var i = 0; i < n; i++)
        {
            var p = parts[i];
            var g = goodParts[i];
            var d = defectParts[i];
            // 数量分摊与个人工时独立；最终时长由事务内按每人的开工记录或补填时间解析。
            var dur = p.DurationMinutes;
            var defectId = d > 0 ? dto.DefectId : null;
            lines.Add((p.UserId, g, d, defectId, dur));
        }
        return lines;
    }

    private static List<(long UserId, int DurationMinutes)> ResolveShareParticipants(ReportDto dto, long currentUserId)
    {
        if (dto.Participants != null && dto.Participants.Count > 0)
        {
            var list = dto.Participants
                .Where(p => p.UserId > 0)
                .GroupBy(p => p.UserId)
                .Select(g => (g.Key, g.Max(x => x.DurationMinutes ?? 0)))
                .ToList();
            if (!list.Any(x => x.Item1 == currentUserId))
                list.Add((currentUserId, 0));
            return list;
        }

        var ids = (dto.ParticipantUserIds ?? new()).Where(x => x > 0).Distinct().ToList();
        if (!ids.Contains(currentUserId)) ids.Add(currentUserId);
        return ids.Select(id => (id, 0)).ToList();
    }

    private static (int[] Goods, int[] Defects) SplitEqual(
        int goodTotal, int defectTotal, List<(long UserId, int DurationMinutes)> parts, long reporterId)
    {
        var n = parts.Count;
        var goodBase = goodTotal / n;
        var goodRem = goodTotal % n;
        var defectBase = defectTotal / n;
        var defectRem = defectTotal % n;
        var goods = new int[n];
        var defects = new int[n];
        for (var i = 0; i < n; i++)
        {
            var isReporter = parts[i].UserId == reporterId;
            goods[i] = goodBase + (isReporter ? goodRem : 0);
            defects[i] = defectBase + (isReporter ? defectRem : 0);
        }
        return (goods, defects);
    }

    /// <summary>
    /// 按时长占比拆分；全员未填 → 均分；有人填了则未填者权重=已填均值（均分兜底拿一份）；尾差归报工人。
    /// 权重仅用于拆数量；未填者 DurationMinutes 仍写 0，避免计时工资误放大。
    /// </summary>
    private static (int[] Goods, int[] Defects) SplitByDuration(
        int goodTotal, int defectTotal, List<(long UserId, int DurationMinutes)> parts, long reporterId)
    {
        var n = parts.Count;
        var positives = parts.Where(p => p.DurationMinutes > 0).Select(p => p.DurationMinutes).ToList();
        if (positives.Count == 0)
            return SplitEqual(goodTotal, defectTotal, parts, reporterId);

        var avgFilled = Math.Max(1, positives.Sum() / positives.Count);
        var weights = parts.Select(p => p.DurationMinutes > 0 ? p.DurationMinutes : avgFilled).ToArray();
        var sumW = weights.Sum();

        var goods = new int[n];
        var defects = new int[n];
        var goodAllocated = 0;
        var defectAllocated = 0;
        for (var i = 0; i < n; i++)
        {
            goods[i] = (int)((long)goodTotal * weights[i] / sumW);
            defects[i] = (int)((long)defectTotal * weights[i] / sumW);
            goodAllocated += goods[i];
            defectAllocated += defects[i];
        }
        var reporterIdx = parts.FindIndex(p => p.UserId == reporterId);
        if (reporterIdx < 0) reporterIdx = 0;
        goods[reporterIdx] += goodTotal - goodAllocated;
        defects[reporterIdx] += defectTotal - defectAllocated;
        return (goods, defects);
    }

    private async Task<List<(long UserId, int GoodQty, int DefectQty, long? DefectId, int DurationMinutes)>> BuildAssigneeLinesAsync(
        ReportDto dto, long factoryId, long operatorId, byte operatorRole)
    {
        var assignees = dto.Assignees ?? new();
        if (assignees.Count == 0)
            throw ThrowHelper.Biz(nameof(SubmitAsync), "代报至少要添加一个被代报的人，不能空着，请添加后再提交。");

        HashSet<long>? groupUserIds = null;
        if (operatorRole == 3)
        {
            var myDepts = await _db.DepartmentUsers.AsNoTracking()
                .Where(x => x.UserId == operatorId)
                .Select(x => x.DepartmentId)
                .ToListAsync();
            groupUserIds = (await _db.DepartmentUsers.AsNoTracking()
                .Where(x => myDepts.Contains(x.DepartmentId))
                .Select(x => x.UserId)
                .Distinct()
                .ToListAsync()).ToHashSet();
        }

        var lines = new List<(long, int, int, long?, int)>();
        var seen = new HashSet<long>();
        foreach (var a in assignees)
        {
            if (a.UserId <= 0)
                throw ThrowHelper.Biz(nameof(SubmitAsync), "代报工中被代报人信息无效，请检查所选人员是否正确后重试。");
            if (!seen.Add(a.UserId))
                throw ThrowHelper.Biz(nameof(SubmitAsync), "同一个被代报人被添加了多次，请去掉重复的、只保留一条。");
            ValidateQty(a.GoodQty, a.DefectQty, a.DefectId, (a.DurationMinutes ?? 0), nameof(SubmitAsync));

            var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == a.UserId)
                ?? throw ThrowHelper.Biz(nameof(SubmitAsync), $"被代报人在系统中不存在（{a.UserId}），请检查人员列表并修正后重试。");
            if (u.FactoryId != factoryId)
                throw ThrowHelper.Biz(nameof(SubmitAsync), $"被代报人 {u.Name} 不属于当前工厂，无法为其代报；请确认该人员所属工厂或切换到对应工厂操作。");
            if (u.Status != 1)
                throw ThrowHelper.Biz(nameof(SubmitAsync), $"被代报人 {u.Name} 已被停用，无法为其代报；请到人员管理中确认该员工状态，或更换为有效人员。");
            if (!await ReportPermissionHelper.CanReportOperationAsync(_db, a.UserId, u.Role, dto.OperationId))
                throw ThrowHelper.Biz(nameof(SubmitAsync), $"{u.Name}没有这道工序的报工权限，不能派给他。请联系管理员给他所在部门加上这道工序的报工权限。");
            if (groupUserIds != null && !groupUserIds.Contains(a.UserId))
                throw ThrowHelper.Biz(nameof(SubmitAsync), $"你无权为其他部门的工人代报工，只能代报本部门人员；请确认 {u.Name} 是否属于你的部门。");

            lines.Add((a.UserId, a.GoodQty, a.DefectQty, a.DefectQty > 0 ? a.DefectId : null, (a.DurationMinutes ?? 0)));
        }
        return lines;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? ex.Message;
        return msg.Contains("uk_report_client_req", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("2627") || msg.Contains("2601")
               || msg.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ApiResult<object?>> WithdrawAsync(long id, long currentUserId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var actor = await _db.Users.AsNoTracking().FirstAsync(x => x.Id == currentUserId);
        await OperationPriceListService.LockFactoryAsync(_db, actor.FactoryId);
        var report = await _db.Reports.FirstOrDefaultAsync(x => x.Id == id && x.FactoryId == actor.FactoryId)
            ?? throw ThrowHelper.BizUser("报工记录不存在");
        if (actor.Role != 1 && report.UserId != currentUserId) throw ThrowHelper.BizUser("你不能撤回别人的报工，只能撤回自己的；如需撤回他人的请联系管理员处理。");
        var order = await _db.WorkOrders.FirstAsync(x => x.Id == report.OrderId);
        await WechatEventService.LockOrderAsync(_db, order);
        await _db.Entry(report).ReloadAsync();
        if (report.SettledFlag || report.ReviewStatus == 1) throw ThrowHelper.BizUser("这条报工已经通过审核或已经结算了，不能撤回，请联系管理员处理。");
        var before = order.Status;
        report.ReviewStatus = 2;
        report.RejectReason = "本人撤回";
        report.ReviewedBy = currentUserId;
        report.ReviewedAt = DateTime.Now;
        await _db.WorkSessions.Where(x => x.ReportId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, (byte)3));
        await _db.SaveChangesAsync();
        await RecalcStatusAsync(order);
        if (_events != null) await _events.EnqueueAsync(order, before);
        await tx.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> UpdateAsync(long id, ReportDto dto, long currentUserId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var actorFactory = await _db.Users.Where(x => x.Id == currentUserId).Select(x => x.FactoryId).SingleAsync();
        await OperationPriceListService.LockFactoryAsync(_db,actorFactory);
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.FactoryId == actorFactory)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "报工记录不存在");

        // 管理员可改任意；班组长/工人仅本人（docs/22）
        var user = await _db.Users.FindAsync(currentUserId)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "当前用户不存在");
        if (user.Role != 1 && report.UserId != currentUserId)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "你没有权限修改别人的报工，只能改自己的；如需修改他人的请联系管理员。");

        if (report.SettledFlag)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "这条报工已经结算了，不能再修改，如有问题请联系管理员处理。");
        // 已通过不可改数，须先退回；待复核/已退回可改
        if (report.ReviewStatus == 1)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "这条报工已经通过审核了，不能直接改。需要先让管理员退回，退回后再修改。");

        if (dto.GoodQty < 0 || dto.DefectQty < 0)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "良品/不良品数不能为负");
        if (dto.DefectQty > 0 && dto.DefectId == null)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "有不良品时必须选择不良品原因");
        if ((dto.DurationMinutes ?? 0) < 0)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "报工时长不能为负");
        if ((dto.DurationMinutes ?? 0) > 1440)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "单次报工时长不能超过24小时");

        var order = await _db.WorkOrders.FirstOrDefaultAsync(o => o.Id == report.OrderId)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "这条报工关联的工单找不到了，可能已被删除。请联系管理员核实工单状态。");
        await WechatEventService.LockOrderAsync(_db, order);
        var beforeStatus = order.Status;
        var task = await _db.WorkOrderOperations
            .FirstOrDefaultAsync(t => t.WorkOrderId == report.OrderId && t.OperationId == report.OperationId);
        var planQty = task?.PlanQty ?? order.Qty;
        var othersGood = await _db.Reports
            .Where(r => r.OrderId == report.OrderId && r.OperationId == report.OperationId
                        && r.Id != report.Id && r.ReviewStatus != 2)
            .SumAsync(r => (int?)r.GoodQty) ?? 0;
        var ratio = (await QtyCfgAsync(order.FactoryId)).OverrunRatio;
        EnsureWithinPlan(othersGood, dto.GoodQty, planQty, ratio, "其它记录已报良品", nameof(UpdateAsync));

        var oldGood = report.GoodQty;
        var oldDefect = report.DefectQty;
        var oldDefectId = report.DefectId;
        var oldDuration = report.DurationMinutes;

        report.GoodQty = dto.GoodQty;
        report.DefectQty = dto.DefectQty;
        report.DefectId = dto.DefectId;
        report.DurationMinutes = dto.DurationMinutes ?? report.DurationMinutes;
        var editType = ReportPriceSnapshot.Calculate(report, 1m).PriceType;
        if (editType == 2 && report.DurationMinutes <= 0) throw ThrowHelper.BizUser("计时报工的时长必须大于0分钟，请把时长改成大于0的数再提交。");
        await _db.WorkSessions.Where(x => x.ReportId == report.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.DurationMinutes, report.DurationMinutes));
        // 改数后回到待复核（含原已退回）
        report.ReviewStatus = 0;
        report.ReviewedBy = null;
        report.ReviewedAt = null;
        report.RejectReason = null;
        // docs/266：改数量、时长仍沿用原工价；缺失快照阻断。
        var storedCoeff = await _db.Users.Where(u => u.Id == report.UserId).Select(u => u.WageCoeff).SingleAsync();
        var calc = ReportPriceSnapshot.Calculate(report, WageCoeffHelper.Coerce(storedCoeff));
        report.PriceSnapshot = ReportPriceSnapshot.WithCoefficient(report,calc.WageCoeff,calc.WageAmount,currentUserId,"report-edit");
        report.UnitPrice = calc.UnitPrice;
        report.WageAmount = calc.WageAmount;

        // docs/205：修改留痕（原值→新值）
        var source = dto.Source == 2 ? (byte)2 : (byte)1;
        _db.ReportChangeLogs.Add(new ProdReportChangeLog
        {
            FactoryId = report.FactoryId,
            ReportId = report.Id,
            ChangedBy = currentUserId,
            OldGoodQty = oldGood,
            OldDefectQty = oldDefect,
            OldDefectId = oldDefectId,
            OldDurationMinutes = oldDuration,
            NewGoodQty = dto.GoodQty,
            NewDefectQty = dto.DefectQty,
            NewDefectId = dto.DefectId,
            NewDurationMinutes = report.DurationMinutes,
            Source = source,
            ChangedAt = DateTime.Now
        });

        await _db.SaveChangesAsync();
        await SaveReportExtAsync(report.Id, dto.Ext);

        await RecalcStatusAsync(order);
        if (_events != null) await _events.EnqueueAsync(order, beforeStatus);
        await tx.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<PageResult<ReportListDto>>> QueryAsync(ReportQueryDto query, long factoryId, long? userId = null)
    {
        var q = from r in _db.Reports.AsNoTracking()
                where r.FactoryId == factoryId
                join o in _db.WorkOrders.AsNoTracking() on r.OrderId equals o.Id
                join p in _db.Products.AsNoTracking() on o.ProductId equals p.Id
                join op in _db.Operations.AsNoTracking() on r.OperationId equals op.Id
                join u in _db.Users.AsNoTracking() on r.UserId equals u.Id
                join d in _db.DefectItems.AsNoTracking() on r.DefectId equals d.Id into dg
                from d in dg.DefaultIfEmpty()
                select new { r, o, p, op, u, DefectName = d == null ? null : d.Name };

        if (!string.IsNullOrWhiteSpace(query.OrderNo))
            q = q.Where(x => x.o.OrderNo.Contains(query.OrderNo));
        if (!string.IsNullOrWhiteSpace(query.ProductCode))
            q = q.Where(x => x.p.Code.Contains(query.ProductCode));
        if (!string.IsNullOrWhiteSpace(query.ProductName))
            q = q.Where(x => x.p.Name.Contains(query.ProductName));
        if (query.ReviewStatus.HasValue)
            q = q.Where(x => x.r.ReviewStatus == query.ReviewStatus.Value);
        if (query.UserType.HasValue)
            q = q.Where(x => x.u.UserType == query.UserType.Value);

        if (userId.HasValue)
            q = q.Where(x => x.r.UserId == userId.Value);

        var total = await q.CountAsync();
        var list = await q.OrderByDescending(x => x.r.ReportTime)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new ReportListDto
            {
                Id = x.r.Id,
                OperationId = x.r.OperationId,
                OrderNo = x.o.OrderNo,
                ProductName = x.p.Name,
                OperationName = x.op.Name,
                UserName = x.u.Name,
                UserType = x.u.UserType,
                GoodQty = x.r.GoodQty,
                DefectQty = x.r.DefectQty,
                DefectId = x.r.DefectId,
                DefectName = x.DefectName,
                DurationMinutes = x.r.DurationMinutes,
                ReviewStatus = x.r.ReviewStatus,
                RejectReason = x.r.RejectReason,
                ReportTime = x.r.ReportTime,
                UnitPrice = x.r.UnitPrice,
                WageAmount = x.r.WageAmount,
                SettledFlag = x.r.SettledFlag
            }).ToListAsync();

        await FillReportExtAsync(list, factoryId);
        return ApiResult<PageResult<ReportListDto>>.Ok(new PageResult<ReportListDto> { List = list, Total = total });
    }

    public async Task<ApiResult<List<DefectItemDto>>> GetDefectsByOperationAsync(long operationId)
    {
        var list = await (from od in _db.OperationDefects.AsNoTracking()
                          where od.OperationId == operationId
                          join d in _db.DefectItems.AsNoTracking() on od.DefectId equals d.Id
                          select new DefectItemDto { Id = d.Id, Name = d.Name }).ToListAsync();
        return ApiResult<List<DefectItemDto>>.Ok(list);
    }

    /// <summary>管理员批量补报未报满工序（docs/17）。不做自动倒冲。</summary>
    public async Task<ApiResult<BatchReportResultDto>> BatchReportAsync(BatchReportDto dto, long currentUserId)
    {
        var user = await _db.Users.FindAsync(currentUserId)
            ?? throw ThrowHelper.Biz(nameof(BatchReportAsync), "当前用户不存在");
        if (user.Role != 1)
            throw ThrowHelper.Biz(nameof(BatchReportAsync), "批量补报只有管理员才能操作，你自己没法补，请联系管理员帮你处理。");

        if (dto.GoodQty < 0)
            throw ThrowHelper.Biz(nameof(BatchReportAsync), "补报的良品数不能填负数，请检查数值后重新提交。");

        var order = await _db.WorkOrders.FirstOrDefaultAsync(o => o.Id == dto.OrderId)
            ?? throw ThrowHelper.Biz(nameof(BatchReportAsync), "工单不存在");

        if (order.FactoryId != user.FactoryId) throw ThrowHelper.BizUser("无权操作其他厂工单");
        var batchNo = string.IsNullOrWhiteSpace(dto.BatchNo) ? null : dto.BatchNo.Trim();
        if (batchNo != null && batchNo.Length > 32)
            throw ThrowHelper.Biz(nameof(BatchReportAsync), "批次号最长32个字符，请缩短后重试。");

        // 幂等：同工单同 batch_no 已存在则直接成功（docs/17 §2）
        if (batchNo != null)
        {
            var existed = await _db.Reports.AsNoTracking()
                .AnyAsync(r => r.OrderId == order.Id && r.BatchNo == batchNo);
            if (existed)
                return ApiResult<BatchReportResultDto>.Ok(new BatchReportResultDto { OperationCount = 0, ReportCount = 0 });
        }

        if (order.Status == 4)
            throw ThrowHelper.Biz(nameof(BatchReportAsync), "工单为草稿，请先在电脑端提交后再报工");
        if (order.Status == 2)
            throw ThrowHelper.Biz(nameof(BatchReportAsync), "这张工单已经做完了，不能再补报了；如有特殊情况找管理员处理。");
        if (order.Status == 3)
            throw ThrowHelper.Biz(nameof(BatchReportAsync), "这张工单已经作废了，不能再补报了；如有误找管理员确认。");

        // 事务外先算待报工序，给拆分校验用；锁内再重查防并发
        var ratio = (await QtyCfgAsync(order.FactoryId)).OverrunRatio;
        var pending = await CollectPendingOpsAsync(order, ratio);
        if (pending.Count == 0)
            throw ThrowHelper.Biz(nameof(BatchReportAsync), "这张工单所有工序都已经报满了，不需要再报了。");

        EnsureQtyWithinRemain(pending, dto.GoodQty, nameof(BatchReportAsync), ratio > 0);
        var perOpUsers = BuildPerOpUsers(pending, dto.Allocations, dto.GoodQty, currentUserId, nameof(BatchReportAsync));

        // docs/150：批量补报同样走上级校验（含 soft/hard）
        await EnsureUpstreamOkForBatchAsync(order, pending, perOpUsers, dto.ConfirmUpstream, nameof(BatchReportAsync));

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            await OperationPriceListService.LockFactoryAsync(_db, order.FactoryId);
            // 工单行 UPDLOCK，防两人同时补报超数（docs/17 §2）
            await WechatEventService.LockOrderAsync(_db, order);
            var beforeStatus = order.Status;

            // 锁内再幂等一次（并发同 batch_no）
            if (batchNo != null)
            {
                var existed = await _db.Reports.AsNoTracking()
                    .AnyAsync(r => r.OrderId == order.Id && r.BatchNo == batchNo);
                if (existed)
                {
                    await tx.CommitAsync();
                    return ApiResult<BatchReportResultDto>.Ok(new BatchReportResultDto { OperationCount = 0, ReportCount = 0 });
                }
            }

            // 锁内重查剩余
            ratio = (await QtyCfgAsync(order.FactoryId)).OverrunRatio;
            pending = await CollectPendingOpsAsync(order, ratio);
            if (pending.Count == 0)
                throw ThrowHelper.Biz(nameof(BatchReportAsync), "所有工序已报满");
            EnsureQtyWithinRemain(pending, dto.GoodQty, nameof(BatchReportAsync), ratio > 0);

            if (order.Status == 4) throw ThrowHelper.BizUser("工单为草稿，请先在电脑端提交后再报工");
            if (order.Status >= 2) throw ThrowHelper.BizUser("这张工单已经结束或作废了，不能再补报，找管理员确认一下情况。");
            var now = DateTime.Now;
            var reportCount = 0;
            var coeffUserIds = perOpUsers.Values.SelectMany(v => v.Select(x => x.UserId)).Distinct().ToList();
            var coeffRows = await _db.Users.AsNoTracking()
                .Where(u => coeffUserIds.Contains(u.Id))
                .Select(u => new { u.Id, u.WageCoeff })
                .ToListAsync();
            var coeffMap = coeffRows.ToDictionary(x => x.Id, x => WageCoeffHelper.Coerce(x.WageCoeff));
            var clock = new WorkSessionService(_db);
            var timedReports = new List<(ProdReport Report, ResolvedWorkTime Time)>();
            foreach (var op in pending)
            {
                if (!perOpUsers.TryGetValue(op.OperationId, out var users))
                    users = new List<(long UserId, int GoodQty)> { (currentUserId, dto.GoodQty) };

                var budget = await new OperationPricingService(_db).BudgetAsync(order.Id, op.OperationId, order.FactoryId);
                var priceType = budget.PriceType;

                foreach (var (uid, qty) in users)
                {
                    var deptId = await ResolveDeptSnapshotAsync(uid, op.OperationId);
                    if (qty == 0 && priceType != 2) throw ThrowHelper.BizUser("非计时岗位的补报良品数必须大于0，请检查填写的数量后重试。");
                    ResolvedWorkTime? time = null;
                    if (priceType == 2)
                    {
                        var input = dto.Allocations?.FirstOrDefault(x => x.OperationId == op.OperationId)?.Users.FirstOrDefault(x => x.UserId == uid);
                        var start = input?.StartTime ?? dto.StartTime;
                        var end = input?.EndTime ?? dto.EndTime;
                        if (!start.HasValue || !end.HasValue) throw ThrowHelper.BizUser("计时岗位的补报必须同时填写开始时间和结束时间，请补全后再提交。");
                        time = await clock.ResolveAsync(order, op.OperationId, uid, currentUserId,
                            new WorkTimeInput { StartTime = start, EndTime = end }, now);
                    }
                    else
                    {
                        var target = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == uid && x.FactoryId == order.FactoryId && x.Status == 1)
                            ?? throw ThrowHelper.BizUser("补报人员在系统中不存在、已停用或不属于当前工厂，请到人员管理中核实该员工状态和归属。");
                        if (!await ReportPermissionHelper.CanReportOperationAsync(_db, uid, target.Role, op.OperationId))
                            throw ThrowHelper.BizUser($"这个人没有这道工序的报工权限，不能批量帮他报工。请联系管理员给他所在部门加上报工权限：{target.Name}");
                    }
                    var wageCoeff = coeffMap.GetValueOrDefault(uid, 1m);
                    var locked = (await new OperationPricingService(_db).LockedAsync(order.Id,op.OperationId,order.FactoryId,time?.ReportTime ?? now)) with { AppliedWageCoeff = budget.PriceType == 3 ? 1m : wageCoeff };
                    var report = new ProdReport
                    {
                        FactoryId = order.FactoryId,
                        OrderId = order.Id,
                        OperationId = op.OperationId,
                        UserId = uid,
                        GoodQty = qty,
                        DefectQty = 0,
                        DefectId = null,
                        DurationMinutes = time?.Minutes ?? 0,
                        BatchNo = batchNo,
                        ProductId = order.ProductId,
                        DepartmentId = deptId,
                        PriceSnapshot = System.Text.Json.JsonSerializer.Serialize(locked),
                        SettledFlag = false,
                        ReviewStatus = 1, // docs/22：管理员批量补报直接已通过
                        ReviewedBy = currentUserId,
                        ReviewedAt = now,
                        ReportTime = time?.ReportTime ?? now
                    };
                    var calc = ReportPriceSnapshot.Calculate(report, wageCoeff);
                    report.UnitPrice = calc.UnitPrice;
                    report.WageAmount = calc.WageAmount;
                    _db.Reports.Add(report);
                    if (time != null) timedReports.Add((report, time));
                    reportCount++;
                }
            }

            var created = _db.ChangeTracker.Entries<ProdReport>().Where(x => x.State == EntityState.Added).Select(x => x.Entity).ToList();
            await _db.SaveChangesAsync();
            foreach (var (report, time) in timedReports) await clock.CompleteAsync(time, report, currentUserId);
            await _db.SaveChangesAsync();
            await RecalcStatusAsync(order);
            if (_events != null) await _events.EnqueueAsync(order, beforeStatus, created);
            await tx.CommitAsync();

            return ApiResult<BatchReportResultDto>.Ok(new BatchReportResultDto
            {
                OperationCount = pending.Count,
                ReportCount = reportCount
            });
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>待报工序：有任务行用任务行；无任务行兜底产品路线 + 工单 qty（与 LoadTasksAsync 同口径）。</summary>
    private async Task<List<(long OperationId, string OperationName, int PlanQty, int Remain)>> CollectPendingOpsAsync(ProdWorkOrder order, int overrunRatio)
    {
        var goodMap = await (from r in _db.Reports.AsNoTracking()
                             where r.OrderId == order.Id && r.ReviewStatus != 2
                             group r by r.OperationId into g
                             select new { OperationId = g.Key, Done = g.Sum(x => x.GoodQty) })
            .ToDictionaryAsync(x => x.OperationId, x => x.Done);

        var tasks = await _db.WorkOrderOperations.AsNoTracking()
            .Where(t => t.WorkOrderId == order.Id)
            .OrderBy(t => t.Seq)
            .ToListAsync();

        List<(long OperationId, int PlanQty, int Seq)> plans;
        if (tasks.Count > 0)
        {
            plans = tasks.Select(t => (t.OperationId, t.PlanQty, t.Seq)).ToList();
        }
        else
        {
            var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == order.ProductId);
            if (product?.RoutingId == null)
                return new List<(long, string, int, int)>();
            var steps = await _db.RoutingSteps.AsNoTracking()
                .Where(s => s.RoutingId == product.RoutingId)
                .OrderBy(s => s.Seq)
                .ToListAsync();
            plans = steps.Select(s => (s.OperationId, order.Qty, s.Seq)).ToList();
        }

        var names = await _db.Operations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name);
        var pending = new List<(long OperationId, string OperationName, int PlanQty, int Remain)>();
        foreach (var (operationId, planQty, _) in plans)
        {
            var done = goodMap.GetValueOrDefault(operationId, 0);
            var remain = ReportQtySettingService.AllowedQty(planQty, overrunRatio) - done;
            if (remain <= 0) continue;
            pending.Add((operationId, names.GetValueOrDefault(operationId, ""), planQty, remain));
        }
        return pending;
    }

    private static void EnsureQtyWithinRemain(
        List<(long OperationId, string OperationName, int PlanQty, int Remain)> pending,
        int goodQty,
        string loc,
        bool overrunOn)
    {
        var bottleneck = pending.OrderBy(p => p.Remain).First();
        if (goodQty <= bottleneck.Remain) return;
        if (overrunOn)
            throw ThrowHelper.Biz(loc,
                $"工序【{bottleneck.OperationName}】能报的数量已经到上限（还能报 {bottleneck.Remain}），报不进去了，找管理员确认。");
        throw ThrowHelper.Biz(loc,
            $"本次数量超过工序【{bottleneck.OperationName}】还能报的上限（{bottleneck.Remain}），请减少数量或找管理员确认。");
    }

    /// <summary>每道待报工序 → 若干 (userId, qty)；未指定挂当前管理员；指定则合计必须 = N。</summary>
    private static Dictionary<long, List<(long UserId, int GoodQty)>> BuildPerOpUsers(
        List<(long OperationId, string OperationName, int PlanQty, int Remain)> pending,
        List<BatchReportOperationDto>? allocations,
        int goodQty,
        long adminUserId,
        string loc)
    {
        var pendingIds = pending.Select(p => p.OperationId).ToHashSet();
        var result = new Dictionary<long, List<(long UserId, int GoodQty)>>();

        if (allocations != null && allocations.Count > 0)
        {
            foreach (var a in allocations)
            {
                if (!pendingIds.Contains(a.OperationId))
                    throw ThrowHelper.Biz(loc, $"工序 {a.OperationId} 不在待报列表中，可能已经报满或不在本次补报范围内，请刷新待报工序列表后重试。");
                if (a.Users == null || a.Users.Count == 0)
                    throw ThrowHelper.Biz(loc, $"工序 {a.OperationId} 的拆分报工需要指定参与人员，请添加拆分人员后再提交。");
                var sum = 0;
                var list = new List<(long UserId, int GoodQty)>();
                foreach (var u in a.Users)
                {
                    if (u.UserId <= 0)
                        throw ThrowHelper.Biz(loc, "拆分报工中指定的人员信息无效，请检查所选人员是否正确后重试。");
                    if (u.GoodQty < 0)
                        throw ThrowHelper.Biz(loc, "拆分报工的数量不能填负数，请检查数值后重新提交。");
                    sum += u.GoodQty;
                    list.Add((u.UserId, u.GoodQty));
                }
                if (sum != goodQty)
                    throw ThrowHelper.Biz(loc, $"工序 {a.OperationId} 拆分各人员数量之和（{sum}）必须等于本次补报总数（{goodQty}），请核对拆分明细后重新提交。");
                result[a.OperationId] = list;
            }
        }

        foreach (var op in pending)
        {
            if (!result.ContainsKey(op.OperationId))
                result[op.OperationId] = new List<(long, int)> { (adminUserId, goodQty) };
        }
        return result;
    }

    /// <summary>部门快照：用户部门 ∩ 工序报工权限部门，取 id 最小（docs/21）。</summary>
    private async Task<long?> ResolveDeptSnapshotAsync(long userId, long operationId)
    {
        var userDepts = await _db.DepartmentUsers.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DepartmentId).ToListAsync();
        if (userDepts.Count == 0) return null;
        var hit = await _db.OperationDepartments.AsNoTracking()
            .Where(x => x.OperationId == operationId && userDepts.Contains(x.DepartmentId))
            .OrderBy(x => x.DepartmentId)
            .Select(x => (long?)x.DepartmentId)
            .FirstOrDefaultAsync();
        return hit;
    }

    private async Task<(byte PriceType, decimal? UnitPrice, decimal? WageAmount, string? Snapshot)> PreviewWageAsync(
        long factoryId, long productId, long operationId, long userId,
        int goodQty, int defectQty, int durationMinutes, long orderId, DateTime? reportTime = null)
    {
        var storedCoeff = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (decimal?)u.WageCoeff)
            .FirstOrDefaultAsync();
        var wageCoeff = storedCoeff is > 0m ? WageCoeffHelper.Coerce(storedCoeff.Value) : 1m;
        var locked = await new OperationPricingService(_db).LockedAsync(orderId, operationId, factoryId, reportTime);
        locked = locked with { AppliedWageCoeff = locked.PriceType == 3 ? 1m : wageCoeff };
        var snapshotJson = System.Text.Json.JsonSerializer.Serialize(locked);
        var result = ReportPriceSnapshot.Calculate(new ProdReport
        {
            FactoryId = factoryId,
            OrderId = orderId,
            OperationId = operationId,
            ProductId = productId,
            GoodQty = goodQty,
            DefectQty = defectQty,
            DurationMinutes = durationMinutes,
            PriceSnapshot = snapshotJson
        }, wageCoeff);
        return (locked.PriceType, result.UnitPrice, result.WageAmount, snapshotJson);
    }

    /// <summary>报工提交前工价只读预览（docs/269）：只算额，不写库、不校验权限、不触发提交/复核链。</summary>
    public async Task<ApiResult<ReportWagePreviewDto>> PreviewWageForSubmitAsync(ReportWagePreviewInputDto dto, long factoryId, long currentUserId)
    {
        var order = await _db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == dto.OrderId && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(PreviewWageForSubmitAsync), "工单不存在");
        var (priceType, unitPrice, wageAmount, _) = await PreviewWageAsync(
            factoryId, order.ProductId, dto.OperationId, currentUserId,
            dto.GoodQty, dto.DefectQty, dto.DurationMinutes, order.Id);
        return ApiResult<ReportWagePreviewDto>.Ok(new ReportWagePreviewDto
        {
            Matched = unitPrice != null,
            UnitPrice = unitPrice,
            WageAmount = wageAmount,
            PriceType = priceType
        });
    }

    private async Task SaveReportExtAsync(long reportId, Dictionary<long, string>? ext)
    {
        if (ext == null || ext.Count == 0) return;
        try
        {
            foreach (var kv in ext)
            {
                var existing = await _db.CustomFieldValues
                    .FirstOrDefaultAsync(v => v.TargetId == reportId && v.FieldId == kv.Key);
                if (existing != null) existing.Value = kv.Value;
                else _db.CustomFieldValues.Add(new SysCustomFieldValue
                {
                    FieldId = kv.Key,
                    TargetId = reportId,
                    Value = kv.Value
                });
            }
            await _db.SaveChangesAsync();
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw ThrowHelper.General(nameof(SaveReportExtAsync), "报工自定义字段保存失败", ex);
        }
    }

    private async Task FillReportExtAsync(List<ReportListDto> list, long factoryId)
    {
        if (list.Count == 0) return;

        var fieldIds = await _db.CustomFields.AsNoTracking()
            .Where(f => f.FactoryId == factoryId && f.Target == "report")
            .Select(f => f.Id)
            .ToListAsync();
        if (fieldIds.Count == 0) return;

        var reportIds = list.Select(x => x.Id).ToList();
        var values = await (
            from v in _db.CustomFieldValues.AsNoTracking()
            join f in _db.CustomFields.AsNoTracking() on v.FieldId equals f.Id
            where fieldIds.Contains(v.FieldId)
                  && reportIds.Contains(v.TargetId)
                  && f.FactoryId == factoryId
                  && f.Target == "report"
            select new { v.FieldId, v.TargetId, v.Value }
        ).ToListAsync();

        var map = values.GroupBy(v => v.TargetId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.FieldId, x => x.Value ?? ""));

        foreach (var item in list)
        {
            if (map.TryGetValue(item.Id, out var ext))
                item.Ext = ext;
        }
    }

    /// <summary>按工序任务重算工单状态（进度轨：排除已退回，docs/22；重开 docs/422）。</summary>
    private async Task RecalcStatusAsync(ProdWorkOrder order)
    {
        if (order.Status == 3) return;

        var hasReport = await _db.Reports.AnyAsync(r => r.OrderId == order.Id && r.ReviewStatus != 2);
        if (!hasReport)
        {
            order.Status = 0;
            order.Reopened = false;
            await _db.SaveChangesAsync();
            return;
        }

        order.Status = 1;

        var tasks = await _db.WorkOrderOperations.Where(t => t.WorkOrderId == order.Id).ToListAsync();
        bool allDone;
        if (tasks.Count == 0)
        {
            var totalGood = await _db.Reports
                .Where(r => r.OrderId == order.Id && r.ReviewStatus != 2)
                .SumAsync(r => (int?)r.GoodQty) ?? 0;
            allDone = totalGood >= order.Qty;
        }
        else
        {
            var reportSum = await (from r in _db.Reports
                                   where r.OrderId == order.Id && r.ReviewStatus != 2
                                   group r by r.OperationId into g
                                   select new { OperationId = g.Key, Done = g.Sum(x => x.GoodQty) })
                                   .ToDictionaryAsync(x => x.OperationId, x => x.Done);
            allDone = tasks.All(t => reportSum.GetValueOrDefault(t.OperationId, 0) >= t.PlanQty);
        }

        if (allDone)
        {
            if (!order.Reopened) order.Status = 2;
        }
        else
        {
            order.Reopened = false;
        }

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// docs/150：与复核页 UpstreamLimit 同口径。
    /// 首道不校验；非首道：紧邻上一道已复核通过良品 &lt; 同工序占用(含本次) → soft 确认 / hard 拒绝。
    /// </summary>
    private async Task EnsureUpstreamOkAsync(
        ProdWorkOrder order, long operationId, int addGoodQty, bool confirmUpstream, string where)
    {
        if (addGoodQty <= 0) return;

        var hit = await EvaluateUpstreamAsync(order, operationId, addGoodQty);
        if (hit == null) return;

        var cfg = await _upstreamSetting.GetOrDefaultAsync(order.FactoryId);
        var hard = ReportUpstreamSettingService.IsHardForOp(cfg, operationId);
        if (hard)
            throw ThrowHelper.Biz(where, $"上一道【{hit.PrevOpName}】还没有复核通过足够的数量，你现在还不能报工。请等上一道工序复核完成后再来报工。");
        if (confirmUpstream) return;
        throw ThrowHelper.BizCode(
            $"上一道【{hit.PrevOpName}】复核通过的数量不太够，你确定要继续报工吗？不确定的话请先跟班组长确认。",
            UpstreamSoftConfirmCode);
    }

    private async Task EnsureUpstreamOkForBatchAsync(
        ProdWorkOrder order,
        List<(long OperationId, string OperationName, int PlanQty, int Remain)> pending,
        Dictionary<long, List<(long UserId, int GoodQty)>> perOpUsers,
        bool confirmUpstream,
        string where)
    {
        var softNames = new List<string>();
        var hardNames = new List<string>();
        var cfg = await _upstreamSetting.GetOrDefaultAsync(order.FactoryId);

        foreach (var op in pending)
        {
            var add = perOpUsers.TryGetValue(op.OperationId, out var users)
                ? users.Sum(u => u.GoodQty)
                : 0;
            if (add <= 0) continue;
            var hit = await EvaluateUpstreamAsync(order, op.OperationId, add);
            if (hit == null) continue;
            if (ReportUpstreamSettingService.IsHardForOp(cfg, op.OperationId))
                hardNames.Add(hit.PrevOpName);
            else
                softNames.Add(hit.PrevOpName);
        }

        if (hardNames.Count > 0)
        {
            var names = string.Join("、", hardNames.Distinct());
            throw ThrowHelper.Biz(where, $"前面几道【{names}】还没有复核通过足够的数量，你现在还不能报工。请等前面的工序都复核完成后再来报工。");
        }
        if (softNames.Count == 0 || confirmUpstream) return;
        {
            var names = string.Join("、", softNames.Distinct());
            throw ThrowHelper.BizCode(
                $"前面几道【{names}】复核通过的数量不太够，你确定要继续报工吗？不确定的话请先跟班组长确认。",
                UpstreamSoftConfirmCode);
        }
    }

    private sealed class UpstreamHit
    {
        public string PrevOpName { get; set; } = "";
    }

    /// <summary>未超限或首道/无法判定顺序 → null；超限 → 含上一道名称。</summary>
    private async Task<UpstreamHit?> EvaluateUpstreamAsync(ProdWorkOrder order, long operationId, int addGoodQty)
    {
        var ops = await LoadOpSequenceAsync(order);
        if (ops == null || ops.Count == 0 || ops.All(o => o.OperationId != operationId))
            return null;

        var curSeq = ops.Where(o => o.OperationId == operationId).Min(o => o.Seq);
        var prev = ops.Where(o => o.Seq < curSeq).OrderByDescending(o => o.Seq).FirstOrDefault();
        if (!ops.Any(o => o.Seq < curSeq))
            return null; // 首道：不校验上级

        var countDefect = (await QtyCfgAsync(order.FactoryId)).CountDefect;
        var approved = _db.Reports.AsNoTracking()
            .Where(r => r.OrderId == order.Id && r.OperationId == prev.OperationId && r.ReviewStatus == 1);
        var limit = countDefect
            ? await approved.SumAsync(r => (int?)(r.GoodQty + r.DefectQty)) ?? 0
            : await approved.SumAsync(r => (int?)r.GoodQty) ?? 0;
        var occupied = await _db.Reports.AsNoTracking()
            .Where(r => r.OrderId == order.Id && r.OperationId == operationId
                        && (r.ReviewStatus == 0 || r.ReviewStatus == 1))
            .SumAsync(r => (int?)r.GoodQty) ?? 0;

        if (occupied + addGoodQty <= limit)
            return null;

        var prevName = await _db.Operations.AsNoTracking()
            .Where(o => o.Id == prev.OperationId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync();
        return new UpstreamHit { PrevOpName = string.IsNullOrWhiteSpace(prevName) ? "上工序" : prevName };
    }

    /// <summary>工序顺序：优先工单任务行，否则产品工艺路线（与 ReviewService 同口径）。</summary>
    private async Task<List<(long OperationId, int Seq)>?> LoadOpSequenceAsync(ProdWorkOrder order)
    {
        var tasks = await _db.WorkOrderOperations.AsNoTracking()
            .Where(t => t.WorkOrderId == order.Id)
            .OrderBy(t => t.Seq)
            .Select(t => new { t.OperationId, t.Seq })
            .ToListAsync();
        if (tasks.Count > 0)
            return tasks.Select(t => (t.OperationId, t.Seq)).ToList();

        var routingId = await _db.Products.AsNoTracking()
            .Where(p => p.Id == order.ProductId)
            .Select(p => p.RoutingId)
            .FirstOrDefaultAsync();
        if (routingId == null) return null;

        var steps = await _db.RoutingSteps.AsNoTracking()
            .Where(s => s.RoutingId == routingId.Value)
            .OrderBy(s => s.Seq)
            .Select(s => new { s.OperationId, s.Seq })
            .ToListAsync();
        return steps.Count == 0 ? null : steps.Select(s => (s.OperationId, s.Seq)).ToList();
    }
}

