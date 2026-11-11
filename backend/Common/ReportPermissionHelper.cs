using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Data;

namespace ahu.MicrosoftMes.Common;

/// <summary>
/// 报工放行：部门报权 或 该工单工序任务已派给此人/此人所属班组；管理员 role=1 豁免。
/// 列表过滤与提交校验共用，避免口径错位。主数据仍按部门配置报权（不按人）。
/// </summary>
public static class ReportPermissionHelper
{
    /// <summary>
    /// 当前用户是否可报该工单工序。
    /// <paramref name="workOrderId"/> 有值时：无报权也可凭本单任务派工放行；无工单上下文时仅看部门报权。
    /// </summary>
    public static async Task<bool> CanReportOperationAsync(
        AppDbContext db, long userId, byte role, long operationId,
        long? workOrderId = null, CancellationToken ct = default)
    {
        if (role == 1) return true;

        if (await HasDeptReportPermissionAsync(db, userId, operationId, ct))
            return true;

        if (workOrderId is > 0)
            return await IsAssignedToOrderOperationAsync(db, userId, workOrderId.Value, operationId, ct);

        return false;
    }

    /// <summary>用户所属部门是否落在该工序报工权限部门内。</summary>
    public static async Task<bool> HasDeptReportPermissionAsync(
        AppDbContext db, long userId, long operationId, CancellationToken ct = default)
    {
        var userDeptIds = await db.DepartmentUsers.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.DepartmentId)
            .ToListAsync(ct);

        if (userDeptIds.Count == 0) return false;

        return await db.OperationDepartments.AsNoTracking()
            .AnyAsync(x => x.OperationId == operationId && userDeptIds.Contains(x.DepartmentId), ct);
    }

    /// <summary>
    /// 该工单工序任务是否已派给此人，或派给此人所在班组。
    /// </summary>
    public static async Task<bool> IsAssignedToOrderOperationAsync(
        AppDbContext db, long userId, long workOrderId, long operationId, CancellationToken ct = default)
    {
        var task = await db.WorkOrderOperations.AsNoTracking()
            .FirstOrDefaultAsync(t => t.WorkOrderId == workOrderId && t.OperationId == operationId, ct);
        if (task == null) return false;

        if (await db.WorkOrderOperationAssignees.AsNoTracking()
                .AnyAsync(a => a.WorkOrderOperationId == task.Id && a.UserId == userId, ct))
            return true;

        if (task.AssigneeUserId == userId)
            return true;

        if (task.AssigneeDepartmentId is long deptId)
        {
            return await db.DepartmentUsers.AsNoTracking()
                .AnyAsync(x => x.UserId == userId && x.DepartmentId == deptId, ct);
        }

        return false;
    }

    /// <summary>
    /// 在候选工序中筛出当前用户可凭部门报权报的工序 Id（不含「仅因派工可报」）。
    /// 有工单任务列表时请用 <see cref="FilterAllowedTasksAsync"/>。
    /// </summary>
    public static async Task<HashSet<long>> FilterAllowedOperationIdsAsync(
        AppDbContext db, long userId, byte role, IEnumerable<long> operationIds, CancellationToken ct = default)
    {
        var ids = operationIds.Distinct().ToList();
        if (ids.Count == 0) return new HashSet<long>();
        if (role == 1) return ids.ToHashSet();

        var userDeptIds = await db.DepartmentUsers.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.DepartmentId)
            .ToListAsync(ct);

        if (userDeptIds.Count == 0) return new HashSet<long>();

        var allowed = await db.OperationDepartments.AsNoTracking()
            .Where(x => ids.Contains(x.OperationId) && userDeptIds.Contains(x.DepartmentId))
            .Select(x => x.OperationId)
            .Distinct()
            .ToListAsync(ct);

        return allowed.ToHashSet();
    }

    /// <summary>
    /// 筛工单工序任务：部门报权 或 本任务已派给此人/其班组。返回可报的 OperationId 集合。
    /// </summary>
    public static async Task<HashSet<long>> FilterAllowedTasksAsync(
        AppDbContext db, long userId, byte role,
        IReadOnlyList<(long TaskId, long OperationId)> tasks, CancellationToken ct = default)
    {
        if (tasks.Count == 0) return new HashSet<long>();
        if (role == 1) return tasks.Select(t => t.OperationId).ToHashSet();

        var opIds = tasks.Select(t => t.OperationId).Distinct();
        var allowed = await FilterAllowedOperationIdsAsync(db, userId, role, opIds, ct);

        var needAssignCheck = tasks.Where(t => !allowed.Contains(t.OperationId)).ToList();
        if (needAssignCheck.Count == 0) return allowed;

        var taskIds = needAssignCheck.Select(t => t.TaskId).ToList();
        var personalAssigned = await db.WorkOrderOperationAssignees.AsNoTracking()
            .Where(a => taskIds.Contains(a.WorkOrderOperationId) && a.UserId == userId)
            .Select(a => a.WorkOrderOperationId)
            .ToListAsync(ct);
        var personalSet = personalAssigned.ToHashSet();

        var userDeptIds = await db.DepartmentUsers.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.DepartmentId)
            .ToListAsync(ct);
        var userDeptSet = userDeptIds.ToHashSet();

        var taskRows = await db.WorkOrderOperations.AsNoTracking()
            .Where(t => taskIds.Contains(t.Id))
            .Select(t => new { t.Id, t.OperationId, t.AssigneeUserId, t.AssigneeDepartmentId })
            .ToListAsync(ct);

        foreach (var t in taskRows)
        {
            if (personalSet.Contains(t.Id)
                || t.AssigneeUserId == userId
                || (t.AssigneeDepartmentId is long d && userDeptSet.Contains(d)))
            {
                allowed.Add(t.OperationId);
            }
        }

        return allowed;
    }
}
