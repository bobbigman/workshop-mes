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
    public ReviewService(AppDbContext db, WechatEventService? events = null) { _db = db; _events = events; }

    public async Task<ApiResult<PageResult<ReviewListDto>>> PendingAsync(ReviewQueryDto query, long factoryId, long currentUserId)
    {
        var reviewer = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId)
            ?? throw ThrowHelper.Biz(nameof(PendingAsync), "当前用户不存在");
        if (reviewer.Role != 1 && reviewer.Role != 3)
            throw ThrowHelper.Biz(nameof(PendingAsync), "无复核权限");

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
        var list = await q.OrderByDescending(x => x.r.ReportTime)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new ReviewListDto
            {
                Id = x.r.Id,
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

        return ApiResult<PageResult<ReviewListDto>>.Ok(new PageResult<ReviewListDto> { List = list, Total = total });
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
        var report = await LoadAndAuthorizeAsync(id, factoryId, currentUserId, nameof(ApproveAsync));
        if (report.ReviewStatus != 0)
            throw ThrowHelper.Biz(nameof(ApproveAsync), "仅待复核记录可通过");

        report.ReviewStatus = 1;
        report.ReviewedBy = currentUserId;
        report.ReviewedAt = DateTime.Now;
        report.RejectReason = null;
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> RejectAsync(long id, RejectDto dto, long factoryId, long currentUserId)
    {
        var reason = (dto.Reason ?? "").Trim();
        if (reason.Length < 1 || reason.Length > 256)
            throw ThrowHelper.Biz(nameof(RejectAsync), "退回原因须 1～256 字");

        await using var tx = await _db.Database.BeginTransactionAsync();
        var report = await LoadAndAuthorizeAsync(id, factoryId, currentUserId, nameof(RejectAsync));
        // 允许从待复核或已通过退回（纠错）；已退回重复退回无意义
        if (report.ReviewStatus == 2)
            throw ThrowHelper.Biz(nameof(RejectAsync), "该报工已是退回状态");
        if (report.SettledFlag)
            throw ThrowHelper.Biz(nameof(RejectAsync), "已结算报工不可退回");

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
        if (dto.Ids == null || dto.Ids.Count == 0)
            throw ThrowHelper.Biz(nameof(BatchApproveAsync), "请选择待复核记录");

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
                throw ThrowHelper.Biz(loc, "无权限复核外部门报工");
        }
        return report;
    }
}
