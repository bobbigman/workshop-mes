using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Data;

namespace ahu.MicrosoftMes.Common;

/// <summary>
/// 报工权限=部门（管理员 role=1 豁免）。列表过滤与提交校验共用，避免口径错位。
/// </summary>
public static class ReportPermissionHelper
{
    /// <summary>当前用户是否可报该工序。</summary>
    public static async Task<bool> CanReportOperationAsync(
        AppDbContext db, long userId, byte role, long operationId, CancellationToken ct = default)
    {
        if (role == 1) return true;

        var userDeptIds = await db.DepartmentUsers.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.DepartmentId)
            .ToListAsync(ct);

        if (userDeptIds.Count == 0) return false;

        return await db.OperationDepartments.AsNoTracking()
            .AnyAsync(x => x.OperationId == operationId && userDeptIds.Contains(x.DepartmentId), ct);
    }

    /// <summary>在候选工序中筛出当前用户可报的工序 Id。</summary>
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
}
