using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IAssignService
{
    Task<ApiResult<List<AssignTaskDto>>> MyTasksAsync(long factoryId, long currentUserId);
    Task<ApiResult<object?>> AssignAsync(long taskId, AssignDto dto, long factoryId, long currentUserId);
    Task<ApiResult<List<AssignWorkerDto>>> WorkersAsync(long factoryId, long currentUserId);
    Task<ApiResult<List<AssignDepartmentDto>>> DepartmentsAsync(long taskId, long factoryId, long currentUserId);
    Task<ApiResult<List<AssignSuggestDto>>> SuggestAsync(long taskId, long factoryId, long currentUserId);
}

public class AssignDto
{
    /// <summary>单人派工（兼容旧端）；与 UserIds 二选一，UserIds 优先。</summary>
    public long? UserId { get; set; }
    /// <summary>多人派工（docs/202）；空数组=取消派工（未带 DepartmentId 时）。</summary>
    public List<long>? UserIds { get; set; }
    /// <summary>派给班组=部门（docs/214）；与人互斥；有值则忽略 UserIds。</summary>
    public long? DepartmentId { get; set; }
}

public class AssignWorkerDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public byte Role { get; set; }
}

public class AssignDepartmentDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>派工推荐（docs/2L）：近 30 天该工序报工次数，其次良品。只读，确认仍走派工。</summary>
public class AssignSuggestDto
{
    public long UserId { get; set; }
    public string Name { get; set; } = "";
    public int ReportCount { get; set; }
    public int GoodQty { get; set; }
}

public class AssignTaskDto
{
    public long TaskId { get; set; }
    public string OrderNo { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string OperationName { get; set; } = "";
    public int PlanQty { get; set; }
    public int DoneQty { get; set; }
    public int RemainQty => Math.Max(0, PlanQty - DoneQty);
    public DateTime? DueDate { get; set; }
    public string DueState { get; set; } = "normal";
    public byte OrderStatus { get; set; }
    /// <summary>personal=派给我的；team=本班组；both=两源都有。</summary>
    public string AssignSource { get; set; } = "personal";
}

public class AssignService : IAssignService
{
    private readonly AppDbContext _db;
    private readonly WechatEventService? _events;

    public AssignService(AppDbContext db, WechatEventService? events = null)
    {
        _db = db;
        _events = events;
    }

    /// <summary>测试/旧调用兼容：单人派工；userId 空=取消。</summary>
    public Task<ApiResult<object?>> AssignAsync(long taskId, long? userId, long factoryId, long currentUserId) =>
        AssignAsync(taskId, new AssignDto
        {
            UserIds = userId is > 0 ? new List<long> { userId.Value } : new List<long>()
        }, factoryId, currentUserId);

    public async Task<ApiResult<List<AssignTaskDto>>> MyTasksAsync(long factoryId, long currentUserId)
    {
        var personalIds = await _db.WorkOrderOperationAssignees.AsNoTracking()
            .Where(a => a.UserId == currentUserId)
            .Select(a => a.WorkOrderOperationId)
            .Distinct()
            .ToListAsync();

        var legacyIds = await _db.WorkOrderOperations.AsNoTracking()
            .Where(t => t.AssigneeUserId == currentUserId && !personalIds.Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync();
        if (legacyIds.Count > 0)
            personalIds = personalIds.Concat(legacyIds).Distinct().ToList();

        var myDeptIds = await _db.DepartmentUsers.AsNoTracking()
            .Where(x => x.UserId == currentUserId)
            .Select(x => x.DepartmentId)
            .ToListAsync();
        var teamIds = myDeptIds.Count == 0
            ? new List<long>()
            : await _db.WorkOrderOperations.AsNoTracking()
                .Where(t => t.AssigneeDepartmentId != null && myDeptIds.Contains(t.AssigneeDepartmentId.Value))
                .Select(t => t.Id)
                .ToListAsync();

        var personalSet = personalIds.ToHashSet();
        var teamSet = teamIds.ToHashSet();
        var taskIds = personalSet.Union(teamSet).ToList();
        if (taskIds.Count == 0)
            return ApiResult<List<AssignTaskDto>>.Ok(new List<AssignTaskDto>());

        var rows = await (from t in _db.WorkOrderOperations.AsNoTracking()
                          where taskIds.Contains(t.Id)
                          join o in _db.WorkOrders.AsNoTracking() on t.WorkOrderId equals o.Id
                          where o.FactoryId == factoryId
                          join p in _db.Products.AsNoTracking() on o.ProductId equals p.Id
                          join op in _db.Operations.AsNoTracking() on t.OperationId equals op.Id
                          orderby o.CreatedAt descending, t.Seq
                          select new
                          {
                              t.Id,
                              o.OrderNo,
                              ProductName = p.Name,
                              OperationName = op.Name,
                              t.PlanQty,
                              o.DueDate,
                              o.Status,
                              t.OperationId
                          }).ToListAsync();

        if (rows.Count == 0)
            return ApiResult<List<AssignTaskDto>>.Ok(new List<AssignTaskDto>());

        var opIds = rows.Select(r => r.OperationId).Distinct().ToList();
        var orderIds = await _db.WorkOrderOperations.AsNoTracking()
            .Where(t => taskIds.Contains(t.Id))
            .Select(t => t.WorkOrderId)
            .Distinct()
            .ToListAsync();

        var sums = await _db.Reports.AsNoTracking()
            .Where(r => r.FactoryId == factoryId && r.ReviewStatus != 2
                        && orderIds.Contains(r.OrderId) && opIds.Contains(r.OperationId))
            .GroupBy(r => new { r.OrderId, r.OperationId })
            .Select(g => new { g.Key.OrderId, g.Key.OperationId, Done = g.Sum(x => x.GoodQty) })
            .ToListAsync();

        var taskMeta = await _db.WorkOrderOperations.AsNoTracking()
            .Where(t => taskIds.Contains(t.Id))
            .Select(t => new { t.Id, t.WorkOrderId, t.OperationId })
            .ToListAsync();
        var doneMap = new Dictionary<long, int>();
        foreach (var t in taskMeta)
        {
            var hit = sums.FirstOrDefault(s => s.OrderId == t.WorkOrderId && s.OperationId == t.OperationId);
            doneMap[t.Id] = hit?.Done ?? 0;
        }

        var now = DateTime.Now;
        var list = rows.Select(r =>
        {
            var pers = personalSet.Contains(r.Id);
            var team = teamSet.Contains(r.Id);
            return new AssignTaskDto
            {
                TaskId = r.Id,
                OrderNo = r.OrderNo,
                ProductName = r.ProductName,
                OperationName = r.OperationName,
                PlanQty = r.PlanQty,
                DoneQty = doneMap.GetValueOrDefault(r.Id, 0),
                DueDate = r.DueDate,
                DueState = DueStateHelper.Calc(r.Status, r.DueDate, now),
                OrderStatus = r.Status,
                AssignSource = pers && team ? "both" : team ? "team" : "personal"
            };
        }).ToList();

        return ApiResult<List<AssignTaskDto>>.Ok(list);
    }

    public async Task<ApiResult<List<AssignWorkerDto>>> WorkersAsync(long factoryId, long currentUserId)
    {
        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId && u.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(WorkersAsync), "当前用户不存在");
        if (me.Role != 1 && me.Role != 3)
            throw ThrowHelper.BizUser("无派工权限");

        var q = _db.Users.AsNoTracking()
            .Where(u => u.FactoryId == factoryId && u.Status == 1 && u.Role != 1);

        if (me.Role == 3)
        {
            var groupIds = await GetGroupUserIdsAsync(currentUserId);
            q = q.Where(u => groupIds.Contains(u.Id));
        }

        var list = await q.OrderBy(u => u.Name)
            .Select(u => new AssignWorkerDto { Id = u.Id, Name = u.Name, Role = u.Role })
            .ToListAsync();
        return ApiResult<List<AssignWorkerDto>>.Ok(list);
    }

    public async Task<ApiResult<List<AssignDepartmentDto>>> DepartmentsAsync(long taskId, long factoryId, long currentUserId)
    {
        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId && u.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(DepartmentsAsync), "当前用户不存在");
        if (me.Role != 1 && me.Role != 3)
            throw ThrowHelper.BizUser("无派工权限");

        var task = await _db.WorkOrderOperations.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId)
            ?? throw ThrowHelper.Biz(nameof(DepartmentsAsync), "工序任务不存在");
        var orderOk = await _db.WorkOrders.AsNoTracking()
            .AnyAsync(o => o.Id == task.WorkOrderId && o.FactoryId == factoryId);
        if (!orderOk)
            throw ThrowHelper.Biz(nameof(DepartmentsAsync), "工单不存在或不属于本厂");

        var allowedDeptIds = await _db.OperationDepartments.AsNoTracking()
            .Where(x => x.OperationId == task.OperationId)
            .Select(x => x.DepartmentId)
            .ToListAsync();
        if (allowedDeptIds.Count == 0)
            return ApiResult<List<AssignDepartmentDto>>.Ok(new List<AssignDepartmentDto>());

        if (me.Role == 3)
        {
            var myDepts = await _db.DepartmentUsers.AsNoTracking()
                .Where(x => x.UserId == currentUserId)
                .Select(x => x.DepartmentId)
                .ToListAsync();
            allowedDeptIds = allowedDeptIds.Intersect(myDepts).ToList();
        }

        var list = await _db.Departments.AsNoTracking()
            .Where(d => d.FactoryId == factoryId && allowedDeptIds.Contains(d.Id))
            .OrderBy(d => d.Name)
            .Select(d => new AssignDepartmentDto { Id = d.Id, Name = d.Name })
            .ToListAsync();
        return ApiResult<List<AssignDepartmentDto>>.Ok(list);
    }

    /// <summary>
    /// 推荐派给谁（docs/2L）。近 30 天该工序有效报工（不含已退回）满 3 次才返回名单。
    /// 按报工人 user_id 的次数降序，其次良品数。候选人=报工权限部门内在职非管理员 ∩ 当前可派范围。
    /// </summary>
    public async Task<ApiResult<List<AssignSuggestDto>>> SuggestAsync(long taskId, long factoryId, long currentUserId)
    {
        const int windowDays = 30;
        const int minReports = 3;

        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId && u.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(SuggestAsync), "当前用户不存在");
        if (me.Role != 1 && me.Role != 3)
            throw ThrowHelper.BizUser("无派工权限");

        var task = await _db.WorkOrderOperations.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId)
            ?? throw ThrowHelper.Biz(nameof(SuggestAsync), "工序任务不存在");
        var orderOk = await _db.WorkOrders.AsNoTracking()
            .AnyAsync(o => o.Id == task.WorkOrderId && o.FactoryId == factoryId);
        if (!orderOk)
            throw ThrowHelper.Biz(nameof(SuggestAsync), "工单不存在或不属于本厂");

        var since = DateTime.Now.AddDays(-windowDays);
        var historyCount = await _db.Reports.AsNoTracking().CountAsync(r =>
            r.FactoryId == factoryId
            && r.OperationId == task.OperationId
            && r.ReviewStatus != 2
            && r.ReportTime >= since);
        if (historyCount < minReports)
            return ApiResult<List<AssignSuggestDto>>.Ok(new List<AssignSuggestDto>());

        var allowedDeptIds = await _db.OperationDepartments.AsNoTracking()
            .Where(x => x.OperationId == task.OperationId)
            .Select(x => x.DepartmentId)
            .ToListAsync();
        if (allowedDeptIds.Count == 0)
            return ApiResult<List<AssignSuggestDto>>.Ok(new List<AssignSuggestDto>());

        HashSet<long>? groupUserIds = null;
        if (me.Role == 3)
            groupUserIds = await GetGroupUserIdsAsync(currentUserId);

        var candidateQuery = _db.DepartmentUsers.AsNoTracking()
            .Where(du => allowedDeptIds.Contains(du.DepartmentId))
            .Join(
                _db.Users.AsNoTracking().Where(u => u.FactoryId == factoryId && u.Status == 1 && u.Role != 1),
                du => du.UserId,
                u => u.Id,
                (du, u) => u.Id)
            .Distinct();
        if (groupUserIds != null)
            candidateQuery = candidateQuery.Where(id => groupUserIds.Contains(id));
        var candidateIds = await candidateQuery.ToListAsync();
        if (candidateIds.Count == 0)
            return ApiResult<List<AssignSuggestDto>>.Ok(new List<AssignSuggestDto>());

        var ranked = await _db.Reports.AsNoTracking()
            .Where(r => r.FactoryId == factoryId
                && r.OperationId == task.OperationId
                && r.ReviewStatus != 2
                && r.ReportTime >= since
                && candidateIds.Contains(r.UserId))
            .GroupBy(r => r.UserId)
            .Select(g => new { UserId = g.Key, ReportCount = g.Count(), GoodQty = g.Sum(x => x.GoodQty) })
            .ToListAsync();
        if (ranked.Count == 0)
            return ApiResult<List<AssignSuggestDto>>.Ok(new List<AssignSuggestDto>());

        var rankedIds = ranked.Select(x => x.UserId).ToList();
        var nameMap = await _db.Users.AsNoTracking()
            .Where(u => rankedIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name);

        var list = ranked
            .OrderByDescending(x => x.ReportCount)
            .ThenByDescending(x => x.GoodQty)
            .ThenBy(x => nameMap.GetValueOrDefault(x.UserId, ""), StringComparer.Ordinal)
            .Select(x => new AssignSuggestDto
            {
                UserId = x.UserId,
                Name = nameMap.GetValueOrDefault(x.UserId, ""),
                ReportCount = x.ReportCount,
                GoodQty = x.GoodQty
            })
            .ToList();
        return ApiResult<List<AssignSuggestDto>>.Ok(list);
    }

    public async Task<ApiResult<object?>> AssignAsync(long taskId, AssignDto dto, long factoryId, long currentUserId)
    {
        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId && u.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(AssignAsync), "当前用户不存在");
        if (me.Role != 1 && me.Role != 3)
            throw ThrowHelper.BizUser("无派工权限");

        var task = await _db.WorkOrderOperations.FirstOrDefaultAsync(t => t.Id == taskId)
            ?? throw ThrowHelper.Biz(nameof(AssignAsync), "工序任务不存在");

        var order = await _db.WorkOrders.FirstOrDefaultAsync(o => o.Id == task.WorkOrderId && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(AssignAsync), "工单不存在或不属于本厂");

        var deptId = dto?.DepartmentId is > 0 ? dto.DepartmentId : null;
        var userIds = deptId != null ? new List<long>() : ResolveUserIds(dto);

        HashSet<long>? groupUserIds = null;
        HashSet<long>? groupDeptIds = null;
        if (me.Role == 3)
        {
            groupUserIds = await GetGroupUserIdsAsync(currentUserId);
            groupDeptIds = (await _db.DepartmentUsers.AsNoTracking()
                .Where(x => x.UserId == currentUserId)
                .Select(x => x.DepartmentId)
                .ToListAsync()).ToHashSet();
        }

        string? targetLabel = null;
        if (deptId != null)
        {
            var dept = await _db.Departments.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == deptId && d.FactoryId == factoryId)
                ?? throw ThrowHelper.Biz(nameof(AssignAsync), "班组不存在或不属于本厂");
            var allowed = await _db.OperationDepartments.AsNoTracking()
                .AnyAsync(x => x.OperationId == task.OperationId && x.DepartmentId == deptId);
            if (!allowed)
                throw ThrowHelper.BizUser("只能派给该工序的报工权限部门");
            if (groupDeptIds != null && !groupDeptIds.Contains(deptId.Value))
                throw ThrowHelper.BizUser($"无权派给外部门班组：{dept.Name}");
            targetLabel = $"班组{dept.Name}";
        }
        else
        {
            foreach (var uid in userIds)
            {
                var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid && u.FactoryId == factoryId)
                    ?? throw ThrowHelper.Biz(nameof(AssignAsync), "执行人不存在或不属于本厂");
                if (user.Status != 1)
                    throw ThrowHelper.BizUser($"执行人已停用：{user.Name}");
                if (user.Role == 1)
                    throw ThrowHelper.BizUser("不能派给管理员");
                if (groupUserIds != null && !groupUserIds.Contains(uid))
                    throw ThrowHelper.BizUser($"无权派给外部门工人：{user.Name}");
            }
            if (userIds.Count > 0)
            {
                var names = await _db.Users.AsNoTracking()
                    .Where(u => userIds.Contains(u.Id))
                    .Select(u => u.Name)
                    .ToListAsync();
                targetLabel = "工人" + string.Join("、", names);
            }
        }

        await using var tx = await _db.Database.BeginTransactionAsync();

        var old = await _db.WorkOrderOperationAssignees.Where(a => a.WorkOrderOperationId == taskId).ToListAsync();
        if (old.Count > 0)
            _db.WorkOrderOperationAssignees.RemoveRange(old);

        foreach (var uid in userIds)
        {
            _db.WorkOrderOperationAssignees.Add(new ProdWorkOrderOperationAssignee
            {
                WorkOrderOperationId = taskId,
                UserId = uid
            });
        }

        task.AssigneeUserId = userIds.Count > 0 ? userIds[0] : null;
        task.AssigneeDepartmentId = deptId;
        await _db.SaveChangesAsync();

        if (targetLabel != null && _events != null)
        {
            var opName = await _db.Operations.AsNoTracking()
                .Where(o => o.Id == task.OperationId)
                .Select(o => o.Name)
                .FirstOrDefaultAsync() ?? "";
            await _events.EnqueueAssignAsync(order, task, opName, targetLabel);
        }

        await tx.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }

    private static List<long> ResolveUserIds(AssignDto? dto)
    {
        if (dto?.UserIds != null)
            return dto.UserIds.Where(x => x > 0).Distinct().ToList();
        if (dto?.UserId is long uid && uid > 0)
            return new List<long> { uid };
        return new List<long>();
    }

    private async Task<HashSet<long>> GetGroupUserIdsAsync(long userId)
    {
        var myDepts = await _db.DepartmentUsers.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.DepartmentId)
            .ToListAsync();
        var ids = await _db.DepartmentUsers.AsNoTracking()
            .Where(x => myDepts.Contains(x.DepartmentId))
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync();
        return ids.ToHashSet();
    }
}
