using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using Microsoft.EntityFrameworkCore;

namespace ahu.MicrosoftMes.Services;

/// <summary>读取工单前验证无厂字段的子表经父对象同厂，错误引用不自动修复。</summary>
public static class TenantDataGuard
{
    public static async Task WorkOrdersAsync(AppDbContext db, long factoryId, long[]? ids = null, CancellationToken ct = default)
    {
        var orders = db.WorkOrders.AsNoTracking().Where(o => o.FactoryId == factoryId);
        if (ids != null) orders = orders.Where(o => ids.Contains(o.Id));
        if (await orders.AnyAsync(o => !db.Products.Any(p => p.Id == o.ProductId && p.FactoryId == factoryId), ct)
            || await db.WorkOrderOperations.AsNoTracking().AnyAsync(t => orders.Any(o => o.Id == t.WorkOrderId)
                && !db.Operations.Any(op => op.Id == t.OperationId && op.FactoryId == factoryId), ct))
            throw ThrowHelper.BizUser("工单产品或工序关联异常，请联系管理员核对。");
    }
}
