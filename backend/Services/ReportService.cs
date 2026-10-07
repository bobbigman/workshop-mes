using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IReportService
{
    Task<ApiResult<object?>> SubmitAsync(ReportDto dto, long currentUserId);
    Task<ApiResult<object?>> UpdateAsync(long id, ReportDto dto, long currentUserId);
    Task<ApiResult<PageResult<ReportListDto>>> QueryAsync(ReportQueryDto query, long factoryId, long? userId = null);
    Task<ApiResult<PageResult<ReportListDto>>> QueryMineAsync(ReportMineQueryDto query, long factoryId, long userId);
    Task<ApiResult<ReportTodaySummaryDto>> TodaySummaryAsync(long factoryId, long userId);
    Task<ApiResult<List<ReportChangeLogDto>>> GetChangeLogsAsync(long reportId, long factoryId, long currentUserId);
    Task<ApiResult<List<ReportCandidateDto>>> GetCandidatesAsync(long operationId, long factoryId, long currentUserId);
    Task<ApiResult<List<DefectItemDto>>> GetDefectsByOperationAsync(long operationId);
    Task<ApiResult<BatchReportResultDto>> BatchReportAsync(BatchReportDto dto, long currentUserId);
}

public class ReportDto
{
    public long OrderId { get; set; }
    public long OperationId { get; set; }
    public int GoodQty { get; set; }
    public int DefectQty { get; set; }
    public long? DefectId { get; set; }
    public int DurationMinutes { get; set; }  // 报工时长（分钟），默认 0
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
    /// <summary>修改来源（docs/205）：1=PC（默认）2=H5；仅 Update 使用。</summary>
    public byte Source { get; set; } = 1;
}

public class ReportAssigneeDto
{
    public long UserId { get; set; }
    public int GoodQty { get; set; }
    public int DefectQty { get; set; }
    public long? DefectId { get; set; }
    public int DurationMinutes { get; set; }
}

public class ReportShareParticipantDto
{
    public long UserId { get; set; }
    public int DurationMinutes { get; set; }
}

public class ReportSubmitItemDto
{
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
public class BatchReportDto
{
    public long OrderId { get; set; }
    public int GoodQty { get; set; }
    public string? BatchNo { get; set; }
    public List<BatchReportOperationDto>? Allocations { get; set; }
}

public class BatchReportOperationDto
{
    public long OperationId { get; set; }
    public List<BatchReportUserDto> Users { get; set; } = new();
}

public class BatchReportUserDto
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
    private readonly AppDbContext _db;
    private readonly WechatEventService? _events;
    public ReportService(AppDbContext db, WechatEventService? events = null) { _db = db; _events = events; }

    public async Task<ApiResult<object?>> SubmitAsync(ReportDto dto, long currentUserId)
    {
        var clientReqId = NormalizeClientRequestId(dto.ClientRequestId);
        var hasParticipants = (dto.Participants != null && dto.Participants.Count > 0)
            || (dto.ParticipantUserIds != null && dto.ParticipantUserIds.Count > 0);
        var hasAssignees = dto.Assignees != null && dto.Assignees.Count > 0;
        if (hasParticipants && hasAssignees)
            throw ThrowHelper.Biz(nameof(SubmitAsync), "分摊与代报不能同时提交");

        var order = await _db.WorkOrders.FirstOrDefaultAsync(o => o.Id == dto.OrderId)
            ?? throw ThrowHelper.Biz(nameof(SubmitAsync), "工单不存在");

        var user = await _db.Users.FindAsync(currentUserId)
            ?? throw ThrowHelper.Biz(nameof(SubmitAsync), "当前用户不存在");
        if (user.FactoryId != order.FactoryId)
            throw ThrowHelper.Biz(nameof(SubmitAsync), "无权操作其他厂工单");

        // 去重：已落库则直接回执（工单已结束也可重放成功）
        if (clientReqId != null)
        {
            var existing = await FindByClientRequestAsync(order.FactoryId, currentUserId, clientReqId);
            if (existing.Count > 0)
            {
                if (!MatchesSubmitContent(existing, dto, currentUserId, hasParticipants, hasAssignees))
                    throw ThrowHelper.Biz(nameof(SubmitAsync), "同一请求标识内容已变更，请换新标识重新提交");
                return ApiResult<object?>.Ok(await BuildSubmitResultAsync(existing, idempotent: true));
            }
        }

        if (order.Status == 2)
            throw ThrowHelper.Biz(nameof(SubmitAsync), "工单已结束，不可报工");
        if (order.Status == 3)
            throw ThrowHelper.Biz(nameof(SubmitAsync), "工单已取消，不可报工");

        // 校验工序属于该工单任务
        var taskExists = await _db.WorkOrderOperations.AnyAsync(t => t.WorkOrderId == order.Id && t.OperationId == dto.OperationId);
        if (taskExists == false)
        {
            var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == order.ProductId);
            var inRoute = product?.RoutingId != null && await _db.RoutingSteps.AnyAsync(s => s.RoutingId == product.RoutingId && s.OperationId == dto.OperationId);
            if (!inRoute)
                throw ThrowHelper.Biz(nameof(SubmitAsync), "该工序不属于此工单");
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
            // 代报：仅管理员/班组长
            if (user.Role != 1 && user.Role != 3)
                throw ThrowHelper.Biz(nameof(SubmitAsync), "仅管理员或班组长可代报工");
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
                throw ThrowHelper.Biz(nameof(SubmitAsync), "无报工权限：您不在该工序的报工权限部门内");
            ValidateQty(dto.GoodQty, dto.DefectQty, dto.DefectId, dto.DurationMinutes, nameof(SubmitAsync));
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
                throw ThrowHelper.Biz(nameof(SubmitAsync), "无报工权限：您不在该工序的报工权限部门内");
            ValidateQty(dto.GoodQty, dto.DefectQty, dto.DefectId, dto.DurationMinutes, nameof(SubmitAsync));
            lines = new List<(long, int, int, long?, int)>
            {
                (currentUserId, dto.GoodQty, dto.DefectQty, dto.DefectId, dto.DurationMinutes)
            };
            operatorUserId = null;
            reviewStatus = user.Role == 1 ? (byte)1 : (byte)0;
            reviewedBy = user.Role == 1 ? currentUserId : null;
            reviewedAt = user.Role == 1 ? now : null;
        }

        var totalGood = lines.Sum(x => x.GoodQty);

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
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

            if (order.Status >= 2) throw ThrowHelper.BizUser("工单已结束或取消，不可报工");
            var task = await _db.WorkOrderOperations
                .FirstOrDefaultAsync(t => t.WorkOrderId == order.Id && t.OperationId == dto.OperationId);
            var planQty = task?.PlanQty ?? order.Qty;
            var alreadyGood = await _db.Reports
                .Where(r => r.OrderId == order.Id && r.OperationId == dto.OperationId && r.ReviewStatus != 2)
                .SumAsync(r => (int?)r.GoodQty) ?? 0;
            if (alreadyGood + totalGood > planQty)
                throw ThrowHelper.Biz(nameof(SubmitAsync),
                    $"超出计划数：该工序计划 {planQty}，已报良品 {alreadyGood}，本次 {totalGood}");

            var created = new List<ProdReport>();
            foreach (var line in lines)
            {
                var deptId = await ResolveDeptSnapshotAsync(line.UserId, dto.OperationId);
                var (unitPrice, wageAmount) = await PreviewWageAsync(
                    order.FactoryId, order.ProductId, dto.OperationId, deptId, line.UserId,
                    line.GoodQty, line.DefectQty, line.DurationMinutes, now);

                var report = new ProdReport
                {
                    FactoryId = order.FactoryId,
                    OrderId = order.Id,
                    OperationId = dto.OperationId,
                    UserId = line.UserId,
                    GoodQty = line.GoodQty,
                    DefectQty = line.DefectQty,
                    DefectId = line.DefectId,
                    DurationMinutes = line.DurationMinutes,
                    ProductId = order.ProductId,
                    DepartmentId = deptId,
                    UnitPrice = unitPrice,
                    WageAmount = wageAmount,
                    SettledFlag = false,
                    ReviewStatus = reviewStatus,
                    ReviewedBy = reviewedBy,
                    ReviewedAt = reviewedAt,
                    ReportTime = now,
                    ClientRequestId = clientReqId,
                    ShareBatchNo = shareBatchNo,
                    OperatorUserId = operatorUserId
                };
                _db.Reports.Add(report);
                created.Add(report);
            }

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
            ?? throw ThrowHelper.Biz(nameof(GetChangeLogsAsync), "报工记录不存在");

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId)
            ?? throw ThrowHelper.Biz(nameof(GetChangeLogsAsync), "当前用户不存在");
        if (user.Role != 1 && report.UserId != currentUserId)
            throw ThrowHelper.Biz(nameof(GetChangeLogsAsync), "无权限查看他人报工修改日志");

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
            throw ThrowHelper.Biz(loc, "良品/不良品数不能为负");
        if (defectQty > 0 && defectId == null)
            throw ThrowHelper.Biz(loc, "有不良品时必须选择不良品原因");
        if (durationMinutes < 0)
            throw ThrowHelper.Biz(loc, "报工时长不能为负");
        if (durationMinutes > 1440)
            throw ThrowHelper.Biz(loc, "单次报工时长不能超过24小时");
    }

    private static string? NormalizeClientRequestId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (s.Length > 64)
            throw ThrowHelper.Biz(nameof(SubmitAsync), "请求标识过长");
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
            var want = (dto.Assignees ?? new())
                .OrderBy(a => a.UserId)
                .Select(a => (a.UserId, a.GoodQty, a.DefectQty, a.DefectId, a.DurationMinutes))
                .ToList();
            var got = existing
                .OrderBy(r => r.UserId)
                .Select(r => (r.UserId, r.GoodQty, r.DefectQty, r.DefectId, r.DurationMinutes))
                .ToList();
            return want.SequenceEqual(got);
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
               && r0.DurationMinutes == dto.DurationMinutes;
    }

    private async Task<object> BuildSubmitResultAsync(List<ProdReport> reports, bool idempotent)
    {
        var userIds = reports.Select(r => r.UserId).Distinct().ToList();
        var opIds = reports.Select(r => r.OperationId).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name);
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
            throw ThrowHelper.Biz(nameof(SubmitAsync), "请至少选择一名参与人");

        foreach (var p in parts)
        {
            var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == p.UserId)
                ?? throw ThrowHelper.Biz(nameof(SubmitAsync), $"参与人不存在：{p.UserId}");
            if (u.Status != 1)
                throw ThrowHelper.Biz(nameof(SubmitAsync), $"参与人已停用：{u.Name}");
            if (!await ReportPermissionHelper.CanReportOperationAsync(_db, p.UserId, u.Role, dto.OperationId))
                throw ThrowHelper.Biz(nameof(SubmitAsync), $"无该工序报工权限：{u.Name}");
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
            // 时长：按工时模式写各人时长；平均模式仍只记报工人，避免计时工资被放大
            var dur = shareMode == 2
                ? p.DurationMinutes
                : (p.UserId == currentUserId ? dto.DurationMinutes : 0);
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
                .Select(g => (g.Key, g.Max(x => x.DurationMinutes)))
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
            throw ThrowHelper.Biz(nameof(SubmitAsync), "请至少添加一名被代报人");

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
                throw ThrowHelper.Biz(nameof(SubmitAsync), "被代报人无效");
            if (!seen.Add(a.UserId))
                throw ThrowHelper.Biz(nameof(SubmitAsync), "同一被代报人不能重复添加");
            ValidateQty(a.GoodQty, a.DefectQty, a.DefectId, a.DurationMinutes, nameof(SubmitAsync));

            var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == a.UserId)
                ?? throw ThrowHelper.Biz(nameof(SubmitAsync), $"被代报人不存在：{a.UserId}");
            if (u.FactoryId != factoryId)
                throw ThrowHelper.Biz(nameof(SubmitAsync), $"被代报人不属于本厂：{u.Name}");
            if (u.Status != 1)
                throw ThrowHelper.Biz(nameof(SubmitAsync), $"被代报人已停用：{u.Name}");
            if (!await ReportPermissionHelper.CanReportOperationAsync(_db, a.UserId, u.Role, dto.OperationId))
                throw ThrowHelper.Biz(nameof(SubmitAsync), $"{u.Name} 无该工序报工权限");
            if (groupUserIds != null && !groupUserIds.Contains(a.UserId))
                throw ThrowHelper.Biz(nameof(SubmitAsync), $"无权为外部门工人代报：{u.Name}");

            lines.Add((a.UserId, a.GoodQty, a.DefectQty, a.DefectQty > 0 ? a.DefectId : null, a.DurationMinutes));
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

    public async Task<ApiResult<object?>> UpdateAsync(long id, ReportDto dto, long currentUserId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "报工记录不存在");

        // 管理员可改任意；班组长/工人仅本人（docs/22）
        var user = await _db.Users.FindAsync(currentUserId)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "当前用户不存在");
        if (user.Role != 1 && report.UserId != currentUserId)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "无权限修改他人报工");

        if (report.SettledFlag)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "已结算报工不可修改");
        // 已通过不可改数，须先退回；待复核/已退回可改
        if (report.ReviewStatus == 1)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "已通过报工不可修改，请先退回后再改");

        if (dto.GoodQty < 0 || dto.DefectQty < 0)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "良品/不良品数不能为负");
        if (dto.DefectQty > 0 && dto.DefectId == null)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "有不良品时必须选择不良品原因");
        if (dto.DurationMinutes < 0)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "报工时长不能为负");
        if (dto.DurationMinutes > 1440)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "单次报工时长不能超过24小时");

        var order = await _db.WorkOrders.FirstOrDefaultAsync(o => o.Id == report.OrderId)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "报工关联工单不存在");
        await WechatEventService.LockOrderAsync(_db, order);
        var beforeStatus = order.Status;
        var task = await _db.WorkOrderOperations
            .FirstOrDefaultAsync(t => t.WorkOrderId == report.OrderId && t.OperationId == report.OperationId);
        var planQty = task?.PlanQty ?? order.Qty;
        var othersGood = await _db.Reports
            .Where(r => r.OrderId == report.OrderId && r.OperationId == report.OperationId
                        && r.Id != report.Id && r.ReviewStatus != 2)
            .SumAsync(r => (int?)r.GoodQty) ?? 0;
        if (othersGood + dto.GoodQty > planQty)
            throw ThrowHelper.Biz(nameof(UpdateAsync),
                $"超出计划数：该工序计划 {planQty}，其它记录已报良品 {othersGood}，本次 {dto.GoodQty}");

        var oldGood = report.GoodQty;
        var oldDefect = report.DefectQty;
        var oldDefectId = report.DefectId;
        var oldDuration = report.DurationMinutes;

        report.GoodQty = dto.GoodQty;
        report.DefectQty = dto.DefectQty;
        report.DefectId = dto.DefectId;
        report.DurationMinutes = dto.DurationMinutes;
        // 改数后回到待复核（含原已退回）
        report.ReviewStatus = 0;
        report.ReviewedBy = null;
        report.ReviewedAt = null;
        report.RejectReason = null;
        // docs/21：重新匹配工价预览
        report.ProductId ??= order.ProductId;
        report.DepartmentId ??= await ResolveDeptSnapshotAsync(report.UserId, report.OperationId);
        var (up, wa) = await PreviewWageAsync(
            report.FactoryId, report.ProductId, report.OperationId, report.DepartmentId, report.UserId,
            dto.GoodQty, dto.DefectQty, dto.DurationMinutes, report.ReportTime);
        report.UnitPrice = up;
        report.WageAmount = wa;

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
            NewDurationMinutes = dto.DurationMinutes,
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
            throw ThrowHelper.Biz(nameof(BatchReportAsync), "无批量补报权限：仅管理员可操作");

        if (dto.GoodQty <= 0)
            throw ThrowHelper.Biz(nameof(BatchReportAsync), "补报良品数必须大于 0");

        var order = await _db.WorkOrders.FirstOrDefaultAsync(o => o.Id == dto.OrderId)
            ?? throw ThrowHelper.Biz(nameof(BatchReportAsync), "工单不存在");

        if (order.FactoryId != user.FactoryId) throw ThrowHelper.BizUser("无权操作其他厂工单");
        var batchNo = string.IsNullOrWhiteSpace(dto.BatchNo) ? null : dto.BatchNo.Trim();
        if (batchNo != null && batchNo.Length > 32)
            throw ThrowHelper.Biz(nameof(BatchReportAsync), "batch_no 最长 32 字符");

        // 幂等：同工单同 batch_no 已存在则直接成功（docs/17 §2）
        if (batchNo != null)
        {
            var existed = await _db.Reports.AsNoTracking()
                .AnyAsync(r => r.OrderId == order.Id && r.BatchNo == batchNo);
            if (existed)
                return ApiResult<BatchReportResultDto>.Ok(new BatchReportResultDto { OperationCount = 0, ReportCount = 0 });
        }

        if (order.Status == 2)
            throw ThrowHelper.Biz(nameof(BatchReportAsync), "工单已结束，不可补报");
        if (order.Status == 3)
            throw ThrowHelper.Biz(nameof(BatchReportAsync), "工单已取消，不可补报");

        // 事务外先算待报工序，给拆分校验用；锁内再重查防并发
        var pending = await CollectPendingOpsAsync(order);
        if (pending.Count == 0)
            throw ThrowHelper.Biz(nameof(BatchReportAsync), "所有工序已报满");

        EnsureQtyWithinRemain(pending, dto.GoodQty, nameof(BatchReportAsync));
        var perOpUsers = BuildPerOpUsers(pending, dto.Allocations, dto.GoodQty, currentUserId, nameof(BatchReportAsync));

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
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
            pending = await CollectPendingOpsAsync(order);
            if (pending.Count == 0)
                throw ThrowHelper.Biz(nameof(BatchReportAsync), "所有工序已报满");
            EnsureQtyWithinRemain(pending, dto.GoodQty, nameof(BatchReportAsync));

            if (order.Status >= 2) throw ThrowHelper.BizUser("工单已结束或取消，不可补报");
            var now = DateTime.Now;
            var reportCount = 0;
            var rules = await _db.PriceRules.AsNoTracking().Where(r => r.FactoryId == order.FactoryId).ToListAsync();
            foreach (var op in pending)
            {
                if (!perOpUsers.TryGetValue(op.OperationId, out var users))
                    users = new List<(long UserId, int GoodQty)> { (currentUserId, dto.GoodQty) };

                foreach (var (uid, qty) in users)
                {
                    var deptId = await ResolveDeptSnapshotAsync(uid, op.OperationId);
                    var rule = PriceRuleMatcher.Match(
                        rules, order.FactoryId, order.ProductId, op.OperationId, deptId, uid, now);
                    var calc = PriceRuleMatcher.Calculate(rule, qty, 0, 0);
                    _db.Reports.Add(new ProdReport
                    {
                        FactoryId = order.FactoryId,
                        OrderId = order.Id,
                        OperationId = op.OperationId,
                        UserId = uid,
                        GoodQty = qty,
                        DefectQty = 0,
                        DefectId = null,
                        DurationMinutes = 0,
                        BatchNo = batchNo,
                        ProductId = order.ProductId,
                        DepartmentId = deptId,
                        UnitPrice = calc.Matched ? calc.UnitPrice : null,
                        WageAmount = calc.Matched ? calc.WageAmount : null,
                        SettledFlag = false,
                        ReviewStatus = 1, // docs/22：管理员批量补报直接已通过
                        ReviewedBy = currentUserId,
                        ReviewedAt = now,
                        ReportTime = now
                    });
                    reportCount++;
                }
            }

            var created = _db.ChangeTracker.Entries<ProdReport>().Where(x => x.State == EntityState.Added).Select(x => x.Entity).ToList();
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
    private async Task<List<(long OperationId, string OperationName, int PlanQty, int Remain)>> CollectPendingOpsAsync(ProdWorkOrder order)
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
            var remain = planQty - done;
            if (remain <= 0) continue;
            pending.Add((operationId, names.GetValueOrDefault(operationId, ""), planQty, remain));
        }
        return pending;
    }

    private static void EnsureQtyWithinRemain(
        List<(long OperationId, string OperationName, int PlanQty, int Remain)> pending,
        int goodQty,
        string loc)
    {
        var bottleneck = pending.OrderBy(p => p.Remain).First();
        if (goodQty > bottleneck.Remain)
            throw ThrowHelper.Biz(loc,
                $"本次数量超过工序【{bottleneck.OperationName}】剩余可报 {bottleneck.Remain}");
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
                    throw ThrowHelper.Biz(loc, $"工序 {a.OperationId} 不在待报列表中（可能已报满）");
                if (a.Users == null || a.Users.Count == 0)
                    throw ThrowHelper.Biz(loc, $"工序 {a.OperationId} 拆分人员不能为空");
                var sum = 0;
                var list = new List<(long UserId, int GoodQty)>();
                foreach (var u in a.Users)
                {
                    if (u.UserId <= 0)
                        throw ThrowHelper.Biz(loc, "拆分人员无效");
                    if (u.GoodQty <= 0)
                        throw ThrowHelper.Biz(loc, "拆分数量必须大于 0");
                    sum += u.GoodQty;
                    list.Add((u.UserId, u.GoodQty));
                }
                if (sum != goodQty)
                    throw ThrowHelper.Biz(loc, $"工序 {a.OperationId} 拆分合计 {sum} 必须等于补报数 {goodQty}");
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

    private async Task<(decimal? UnitPrice, decimal? WageAmount)> PreviewWageAsync(
        long factoryId, long? productId, long operationId, long? departmentId, long userId,
        int goodQty, int defectQty, int durationMinutes, DateTime atTime)
    {
        var rules = await _db.PriceRules.AsNoTracking().Where(r => r.FactoryId == factoryId).ToListAsync();
        var rule = PriceRuleMatcher.Match(rules, factoryId, productId, operationId, departmentId, userId, atTime);
        var calc = PriceRuleMatcher.Calculate(rule, goodQty, defectQty, durationMinutes);
        if (!calc.Matched) return (null, null);
        return (calc.UnitPrice, calc.WageAmount);
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

    /// <summary>按工序任务重算工单状态（进度轨：排除已退回，docs/22）。</summary>
    private async Task RecalcStatusAsync(ProdWorkOrder order)
    {
        if (order.Status == 3) return;

        var hasReport = await _db.Reports.AnyAsync(r => r.OrderId == order.Id && r.ReviewStatus != 2);
        if (!hasReport)
        {
            order.Status = 0;
            await _db.SaveChangesAsync();
            return;
        }

        order.Status = 1;

        var tasks = await _db.WorkOrderOperations.Where(t => t.WorkOrderId == order.Id).ToListAsync();
        if (tasks.Count == 0)
        {
            var totalGood = await _db.Reports
                .Where(r => r.OrderId == order.Id && r.ReviewStatus != 2)
                .SumAsync(r => (int?)r.GoodQty) ?? 0;
            if (totalGood >= order.Qty) order.Status = 2;
        }
        else
        {
            var reportSum = await (from r in _db.Reports
                                   where r.OrderId == order.Id && r.ReviewStatus != 2
                                   group r by r.OperationId into g
                                   select new { OperationId = g.Key, Done = g.Sum(x => x.GoodQty) })
                                   .ToDictionaryAsync(x => x.OperationId, x => x.Done);
            var allDone = tasks.All(t => reportSum.GetValueOrDefault(t.OperationId, 0) >= t.PlanQty);
            if (allDone) order.Status = 2;
        }

        await _db.SaveChangesAsync();
    }
}
