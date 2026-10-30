using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IReviewService
{
    Task<ApiResult<PageResult<ReviewListDto>>> PendingAsync(ReviewQueryDto query, long factoryId, long currentUserId);
    Task<ApiResult<List<ReviewOrderOptionDto>>> OrderOptionsAsync(long factoryId, long currentUserId);
    Task<ApiResult<object?>> ApproveAsync(long id, long factoryId, long currentUserId);
    Task<ApiResult<object?>> RejectAsync(long id, RejectDto dto, long factoryId, long currentUserId);
    Task<ApiResult<object?>> BatchApproveAsync(BatchApproveDto dto, long factoryId, long currentUserId);
}

public class ReviewQueryDto
{
    public string? OrderNo { get; set; }
    public string? UserName { get; set; }
    public byte? ReviewStatus { get; set; } = 0; // 默认待复核
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class RejectDto
{
    public string Reason { get; set; } = "";
}

public class BatchApproveDto
{
    public List<long> Ids { get; set; } = new();
}

public class ReviewListDto
{
    public long Id { get; set; }
    public string OrderNo { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string OperationName { get; set; } = "";
    public string UserName { get; set; } = "";
    public long UserId { get; set; }
    public int GoodQty { get; set; }
    public int DefectQty { get; set; }
    public int DurationMinutes { get; set; }
    public byte ReviewStatus { get; set; }
    public string? RejectReason { get; set; }
    public DateTime ReportTime { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? WageAmount { get; set; }
    /// <summary>可报上限。首道=工序计划按超报比例向上取整；非首道=紧邻上一道已复核通过量（默认良品，计入时含不良）。无法判定工序顺序时为 null。</summary>
    public int? UpstreamLimit { get; set; }
    /// <summary>同工单同工序已通过+待复核良品合计（含本笔）。</summary>
    public int SameOpOccupiedQty { get; set; }
    /// <summary>超过上道上限，或超过本道允许件数。</summary>
    public bool QtyAbnormal { get; set; }
}

/// <summary>复核页工单下拉：有待复核报工的工单（去重）。</summary>
public class ReviewOrderOptionDto
{
    public string OrderNo { get; set; } = "";
    public string ProductName { get; set; } = "";
}

public class ReviewService : IReviewService
{
    private readonly AppDbContext _db;
    private readonly WechatEventService? _events;
    private readonly IReportQtySettingService _qtySetting;
    public ReviewService(AppDbContext db, IReportQtySettingService qtySetting, WechatEventService? events = null)
    {
        _db = db;
        _qtySetting = qtySetting;
        _events = events;
    }

    public async Task<ApiResult<PageResult<ReviewListDto>>> PendingAsync(ReviewQueryDto query, long factoryId, long currentUserId)
    {
        var reviewer = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId)
            ?? throw ThrowHelper.Biz(nameof(PendingAsync), "当前用户不存在");
        if (reviewer.Role != 1 && reviewer.Role != 3)
            throw ThrowHelper.Biz(nameof(PendingAsync), "当前账号没有复核权限。请到「用户管理」确认该账号是否已分配复核角色，或由有权限的管理员执行复核。");

        var q = from r in _db.Reports.AsNoTracking()
                where r.FactoryId == factoryId
                join o in _db.WorkOrders.AsNoTracking() on r.OrderId equals o.Id
                join p in _db.Products.AsNoTracking() on o.ProductId equals p.Id
                join op in _db.Operations.AsNoTracking() on r.OperationId equals op.Id
                join u in _db.Users.AsNoTracking() on r.UserId equals u.Id
                select new { r, o, p, op, u };

        var status = query.ReviewStatus ?? 0;
        q = q.Where(x => x.r.ReviewStatus == status);

        if (!string.IsNullOrWhiteSpace(query.OrderNo))
            q = q.Where(x => x.o.OrderNo.Contains(query.OrderNo));
        if (!string.IsNullOrWhiteSpace(query.UserName))
            q = q.Where(x => x.u.Name.Contains(query.UserName));

        // 班组长：本组 = 自己部门 ∩ 报工人部门非空（docs/22）
        if (reviewer.Role == 3)
        {
            var myDeptIds = await _db.DepartmentUsers.AsNoTracking()
                .Where(x => x.UserId == currentUserId)
                .Select(x => x.DepartmentId).ToListAsync();
            var allowedUserIds = await _db.DepartmentUsers.AsNoTracking()
                .Where(x => myDeptIds.Contains(x.DepartmentId))
                .Select(x => x.UserId).Distinct().ToListAsync();
            q = q.Where(x => allowedUserIds.Contains(x.r.UserId));
        }

        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(x => x.r.ReportTime)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new
            {
                x.r.Id,
                OrderId = x.o.Id,
                OrderQty = x.o.Qty,
                ProductId = x.o.ProductId,
                OperationId = x.r.OperationId,
                OrderNo = x.o.OrderNo,
                ProductName = x.p.Name,
                OperationName = x.op.Name,
                UserName = x.u.Name,
                UserId = x.r.UserId,
                GoodQty = x.r.GoodQty,
                DefectQty = x.r.DefectQty,
                DurationMinutes = x.r.DurationMinutes,
                ReviewStatus = x.r.ReviewStatus,
                RejectReason = x.r.RejectReason,
                ReportTime = x.r.ReportTime,
                UnitPrice = x.r.UnitPrice,
                WageAmount = x.r.WageAmount
            }).ToListAsync();

        var list = rows.Select(x => new ReviewListDto
        {
            Id = x.Id,
            OrderNo = x.OrderNo,
            ProductName = x.ProductName,
            OperationName = x.OperationName,
            UserName = x.UserName,
            UserId = x.UserId,
            GoodQty = x.GoodQty,
            DefectQty = x.DefectQty,
            DurationMinutes = x.DurationMinutes,
            ReviewStatus = x.ReviewStatus,
            RejectReason = x.RejectReason,
            ReportTime = x.ReportTime,
            UnitPrice = x.UnitPrice,
            WageAmount = x.WageAmount
        }).ToList();

        await FillQtyCheckAsync(factoryId, list, rows.Select(x => (x.Id, x.OrderId, x.OrderQty, x.ProductId, x.OperationId)).ToList());

        return ApiResult<PageResult<ReviewListDto>>.Ok(new PageResult<ReviewListDto> { List = list, Total = total });
    }

    /// <summary>
    /// 尺子②：首道=工序计划按超报比例向上取整；非首道=紧邻上一道已复核通过量（默认良品，计入时含不良）。
    /// 尺子①：同工序已通过+待复核良品 &gt; 本道允许件数，也标异常。两条并存。
    /// </summary>
    private async Task FillQtyCheckAsync(
        long factoryId,
        List<ReviewListDto> list,
        List<(long Id, long OrderId, int OrderQty, long ProductId, long OperationId)> keys)
    {
        if (list.Count == 0) return;

        var qtyCfg = await _qtySetting.GetOrDefaultAsync(factoryId);
        var byId = keys.ToDictionary(x => x.Id);
        var orderIds = keys.Select(x => x.OrderId).Distinct().ToList();

        // 工序顺序：优先工单任务行，否则产品工艺路线
        var taskRows = await _db.WorkOrderOperations.AsNoTracking()
            .Where(t => orderIds.Contains(t.WorkOrderId))
            .Select(t => new { t.WorkOrderId, t.OperationId, t.Seq, t.PlanQty })
            .ToListAsync();
        var tasksByOrder = taskRows.GroupBy(t => t.WorkOrderId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Seq).ToList());

        var needRouteOrderIds = orderIds.Where(oid => !tasksByOrder.ContainsKey(oid)).ToList();
        var routeOpsByOrder = new Dictionary<long, List<(long OperationId, int Seq)>>();
        if (needRouteOrderIds.Count > 0)
        {
            var productIds = keys.Where(k => needRouteOrderIds.Contains(k.OrderId))
                .Select(k => k.ProductId).Distinct().ToList();
            var products = await _db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .Select(p => new { p.Id, p.RoutingId })
                .ToListAsync();
            var productRoute = products.ToDictionary(p => p.Id, p => p.RoutingId);
            var routingIds = products.Where(p => p.RoutingId != null).Select(p => p.RoutingId!.Value).Distinct().ToList();
            var steps = routingIds.Count == 0
                ? new List<(long RoutingId, long OperationId, int Seq)>()
                : (await _db.RoutingSteps.AsNoTracking()
                    .Where(s => routingIds.Contains(s.RoutingId))
                    .Select(s => new { s.RoutingId, s.OperationId, s.Seq })
                    .ToListAsync())
                    .Select(s => (s.RoutingId, s.OperationId, s.Seq)).ToList();
            var stepsByRoute = steps.GroupBy(s => s.RoutingId)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Seq).ToList());

            foreach (var oid in needRouteOrderIds)
            {
                var pid = keys.First(k => k.OrderId == oid).ProductId;
                if (!productRoute.TryGetValue(pid, out var rid) || rid == null) continue;
                if (!stepsByRoute.TryGetValue(rid.Value, out var slist) || slist.Count == 0) continue;
                routeOpsByOrder[oid] = slist.Select(s => (s.OperationId, s.Seq)).ToList();
            }
        }

        // 已通过良品（上工序上限）+ 已通过/待复核良品（同工序占用）
        var reportAgg = await _db.Reports.AsNoTracking()
            .Where(r => orderIds.Contains(r.OrderId) && (r.ReviewStatus == 0 || r.ReviewStatus == 1))
            .GroupBy(r => new { r.OrderId, r.OperationId })
            .Select(g => new
            {
                g.Key.OrderId,
                g.Key.OperationId,
                ApprovedGood = g.Sum(x => x.ReviewStatus == 1 ? x.GoodQty : 0),
                ApprovedDefect = g.Sum(x => x.ReviewStatus == 1 ? x.DefectQty : 0),
                OccupiedGood = g.Sum(x => x.GoodQty)
            }).ToListAsync();
        var approvedMap = reportAgg.ToDictionary(x => (x.OrderId, x.OperationId), x => x.ApprovedGood);
        var approvedDefectMap = reportAgg.ToDictionary(x => (x.OrderId, x.OperationId), x => x.ApprovedDefect);
        var occupiedMap = reportAgg.ToDictionary(x => (x.OrderId, x.OperationId), x => x.OccupiedGood);
        var planMap = new Dictionary<(long OrderId, long OperationId), int>();
        foreach (var t in taskRows)
            planMap[(t.WorkOrderId, t.OperationId)] = t.PlanQty;

        foreach (var dto in list)
        {
            if (!byId.TryGetValue(dto.Id, out var key)) continue;

            List<(long OperationId, int Seq)>? ops = null;
            if (tasksByOrder.TryGetValue(key.OrderId, out var tasks))
                ops = tasks.Select(t => (t.OperationId, t.Seq)).ToList();
            else if (routeOpsByOrder.TryGetValue(key.OrderId, out var routeOps))
                ops = routeOps;

            var planBase = planMap.TryGetValue((key.OrderId, key.OperationId), out var planQty)
                ? planQty
                : key.OrderQty;
            var allowed = ReportQtySettingService.AllowedQty(planBase, qtyCfg.OverrunRatio);

            int? limit = null;
            if (ops != null && ops.Count > 0 && ops.Any(o => o.OperationId == key.OperationId))
            {
                var curSeq = ops.Where(o => o.OperationId == key.OperationId).Min(o => o.Seq);
                var prev = ops.Where(o => o.Seq < curSeq).OrderByDescending(o => o.Seq).FirstOrDefault();
                if (!ops.Any(o => o.Seq < curSeq))
                    limit = allowed; // 首道：工序计划按超报比例
                else
                {
                    approvedMap.TryGetValue((key.OrderId, prev.OperationId), out var approved);
                    if (qtyCfg.CountDefect)
                    {
                        approvedDefectMap.TryGetValue((key.OrderId, prev.OperationId), out var defect);
                        limit = approved + defect;
                    }
                    else
                        limit = approved;
                }
            }

            occupiedMap.TryGetValue((key.OrderId, key.OperationId), out var occupied);
            dto.UpstreamLimit = limit;
            dto.SameOpOccupiedQty = occupied;
            var overUpstream = limit != null && occupied > limit.Value;
            var overPlan = occupied > allowed;
            dto.QtyAbnormal = overUpstream || overPlan;
        }
    }

    public async Task<ApiResult<List<ReviewOrderOptionDto>>> OrderOptionsAsync(long factoryId, long currentUserId)
    {
        var reviewer = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId)
            ?? throw ThrowHelper.Biz(nameof(OrderOptionsAsync), "当前用户不存在");
        if (reviewer.Role != 1 && reviewer.Role != 3)
            throw ThrowHelper.Biz(nameof(OrderOptionsAsync), "无复核权限");

        var q = from r in _db.Reports.AsNoTracking()
                where r.FactoryId == factoryId && r.ReviewStatus == 0
                join o in _db.WorkOrders.AsNoTracking() on r.OrderId equals o.Id
                join p in _db.Products.AsNoTracking() on o.ProductId equals p.Id
                select new { r, o, p };

        if (reviewer.Role == 3)
        {
            var myDeptIds = await _db.DepartmentUsers.AsNoTracking()
                .Where(x => x.UserId == currentUserId)
                .Select(x => x.DepartmentId).ToListAsync();
            var allowedUserIds = await _db.DepartmentUsers.AsNoTracking()
                .Where(x => myDeptIds.Contains(x.DepartmentId))
                .Select(x => x.UserId).Distinct().ToListAsync();
            q = q.Where(x => allowedUserIds.Contains(x.r.UserId));
        }

        var list = await q
            .GroupBy(x => new { x.o.OrderNo, ProductName = x.p.Name })
            .Select(g => new ReviewOrderOptionDto
            {
                OrderNo = g.Key.OrderNo,
                ProductName = g.Key.ProductName
            })
            .OrderBy(x => x.OrderNo)
            .ToListAsync();

        return ApiResult<List<ReviewOrderOptionDto>>.Ok(list);
    }

    public async Task<ApiResult<object?>> ApproveAsync(long id, long factoryId, long currentUserId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        await OperationPriceListService.LockFactoryAsync(_db,factoryId);
        var report = await LoadAndAuthorizeAsync(id, factoryId, currentUserId, nameof(ApproveAsync));
        if (report.ReviewStatus != 0)
            throw ThrowHelper.Biz(nameof(ApproveAsync), "只有状态为「待复核」的记录才能执行通过操作。请确认所选记录当前确实处于待复核状态。");

        report.ReviewStatus = 1;
        report.ReviewedBy = currentUserId;
        report.ReviewedAt = DateTime.Now;
        report.RejectReason = null;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> RejectAsync(long id, RejectDto dto, long factoryId, long currentUserId)
    {
        var reason = (dto.Reason ?? "").Trim();
        if (reason.Length < 1 || reason.Length > 256)
            throw ThrowHelper.Biz(nameof(RejectAsync), "退回时必须填写退回原因，长度在1到256字之间。请在退回原因栏填写具体说明后再提交。");

        await using var tx = await _db.Database.BeginTransactionAsync();
        await OperationPriceListService.LockFactoryAsync(_db,factoryId);
        var report = await LoadAndAuthorizeAsync(id, factoryId, currentUserId, nameof(RejectAsync));
        // 允许从待复核或已通过退回（纠错）；已退回重复退回无意义
        if (report.ReviewStatus == 2)
            throw ThrowHelper.Biz(nameof(RejectAsync), "这条报工已经是退回状态了，不需要再退回。如需重新处理，请让报工人修改后重新提交。");
        if (report.SettledFlag)
            throw ThrowHelper.Biz(nameof(RejectAsync), "这条报工已经结算了，不能退回，如有问题请联系管理员处理。");

        var order = await _db.WorkOrders.FirstOrDefaultAsync(o => o.Id == report.OrderId);
        if (order != null) await WechatEventService.LockOrderAsync(_db, order);
        report.ReviewStatus = 2;
        report.ReviewedBy = currentUserId;
        report.ReviewedAt = DateTime.Now;
        report.RejectReason = reason;
        await _db.SaveChangesAsync();

        // 退回后进度回落，重算工单状态
        if (order != null)
        {
            var wo = new WorkOrderService(_db, _events);
            await wo.RecalcStatusAsync(order);
        }
        await tx.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> BatchApproveAsync(BatchApproveDto dto, long factoryId, long currentUserId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        await OperationPriceListService.LockFactoryAsync(_db,factoryId);
        if (dto.Ids == null || dto.Ids.Count == 0)
            throw ThrowHelper.Biz(nameof(BatchApproveAsync), "批量通过前需要先勾选待复核记录。请在列表中选中至少一条待复核记录后再点批量通过。");

        var reviewer = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId)
            ?? throw ThrowHelper.Biz(nameof(BatchApproveAsync), "当前用户不存在");
        if (reviewer.Role != 1 && reviewer.Role != 3)
            throw ThrowHelper.Biz(nameof(BatchApproveAsync), "无复核权限");

        HashSet<long>? allowedUserIds = null;
        if (reviewer.Role == 3)
        {
            var myDeptIds = await _db.DepartmentUsers.AsNoTracking()
                .Where(x => x.UserId == currentUserId)
                .Select(x => x.DepartmentId).ToListAsync();
            allowedUserIds = (await _db.DepartmentUsers.AsNoTracking()
                .Where(x => myDeptIds.Contains(x.DepartmentId))
                .Select(x => x.UserId).Distinct().ToListAsync()).ToHashSet();
        }

        var reports = await _db.Reports
            .Where(r => r.FactoryId == factoryId && dto.Ids.Contains(r.Id) && r.ReviewStatus == 0)
            .ToListAsync();

        var now = DateTime.Now;
        var n = 0;
        foreach (var r in reports)
        {
            if (allowedUserIds != null && !allowedUserIds.Contains(r.UserId))
                continue;
            r.ReviewStatus = 1;
            r.ReviewedBy = currentUserId;
            r.ReviewedAt = now;
            r.RejectReason = null;
            n++;
        }
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return ApiResult<object?>.Ok(new { approved = n });
    }

    private async Task<ProdReport> LoadAndAuthorizeAsync(long id, long factoryId, long currentUserId, string loc)
    {
        var reviewer = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId)
            ?? throw ThrowHelper.Biz(loc, "当前用户不存在");
        if (reviewer.Role != 1 && reviewer.Role != 3)
            throw ThrowHelper.Biz(loc, "无复核权限");

        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(loc, "报工记录不存在");

        if (reviewer.Role == 3)
        {
            var myDeptIds = await _db.DepartmentUsers.AsNoTracking()
                .Where(x => x.UserId == currentUserId)
                .Select(x => x.DepartmentId).ToListAsync();
            var sameGroup = await _db.DepartmentUsers.AsNoTracking()
                .AnyAsync(x => x.UserId == report.UserId && myDeptIds.Contains(x.DepartmentId));
            if (!sameGroup)
                throw ThrowHelper.Biz(loc, "你没有权限复核其他部门的报工，只能复核本部门的；如需复核外部门的请联系管理员。");
        }
        return report;
    }
}
