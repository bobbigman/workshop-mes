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
}

public class AssignDto
{
    /// <summary>单人派工（兼容旧端）；与 UserIds 二选一，UserIds 优先。</summary>
    public long? UserId { get; set; }
    /// <summary>多人派工（docs/202）；空数组=取消派工。</summary>
    public List<long>? UserIds { get; set; }
}

public class AssignWorkerDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public byte Role { get; set; }
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
}

public class AssignService : IAssignService
{
    private readonly AppDbContext _db;
    public AssignService(AppDbContext db) { _db = db; }

    public async Task<ApiResult<List<AssignTaskDto>>> MyTasksAsync(long factoryId, long currentUserId)
    {
        // 派给我的工序（关联表真源；兼容仅写了 assignee_user_id 的旧行）
        var taskIds = await _db.WorkOrderOperationAssignees.AsNoTracking()
            .Where(a => a.UserId == currentUserId)
            .Select(a => a.WorkOrderOperationId)
            .Distinct()
            .ToListAsync();

        var legacyIds = await _db.WorkOrderOperations.AsNoTracking()
            .Where(t => t.AssigneeUserId == currentUserId && !taskIds.Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync();
        if (legacyIds.Count > 0)
            taskIds = taskIds.Concat(legacyIds).Distinct().ToList();

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
        var list = rows.Select(r => new AssignTaskDto
        {
            TaskId = r.Id,
            OrderNo = r.OrderNo,
            ProductName = r.ProductName,
            OperationName = r.OperationName,
            PlanQty = r.PlanQty,
            DoneQty = doneMap.GetValueOrDefault(r.Id, 0),
            DueDate = r.DueDate,
            DueState = DueStateHelper.Calc(r.Status, r.DueDate, now),
            OrderStatus = r.Status
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

    public async Task<ApiResult<object?>> AssignAsync(long taskId, AssignDto dto, long factoryId, long currentUserId)
    {
        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId && u.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(AssignAsync), "当前用户不存在");
        if (me.Role != 1 && me.Role != 3)
            throw ThrowHelper.BizUser("无派工权限");

        var task = await _db.WorkOrderOperations.FirstOrDefaultAsync(t => t.Id == taskId)
            ?? throw ThrowHelper.Biz(nameof(AssignAsync), "工序任务不存在");

        var order = await _db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == task.WorkOrderId && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(AssignAsync), "工单不存在或不属于本厂");

        var userIds = ResolveUserIds(dto);
        HashSet<long>? groupUserIds = null;
        if (me.Role == 3)
            groupUserIds = await GetGroupUserIdsAsync(currentUserId);

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

        // 冗余首个，兼容旧端只读 assignee_user_id
        task.AssigneeUserId = userIds.Count > 0 ? userIds[0] : null;
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    private static List<long> ResolveUserIds(AssignDto dto)
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
