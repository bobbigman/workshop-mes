using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;

namespace ahu.MicrosoftMes.Services;

/// <summary>
/// 批量加载工单工序与报工汇总，供列表/看板/监控复用，避免逐单 N+1。
/// </summary>
public static class WorkOrderProgressQuery
{
    public sealed class OrderKey
    {
        public long Id { get; set; }
        public long ProductId { get; set; }
        public int Qty { get; set; }
    }

    public sealed class OpRow
    {
        public long? TaskId { get; set; }
        public long OperationId { get; set; }
        public string OperationName { get; set; } = "";
        public int Seq { get; set; }
        public int PlanQty { get; set; }
        public int DoneQty { get; set; }
        public int DefectQty { get; set; }
        public long? AssigneeUserId { get; set; }
        public string AssigneeName { get; set; } = "";
        /// <summary>true=无任务行时按路线生成的虚拟工序，不可直接派工。</summary>
        public bool IsVirtual { get; set; }
        public string Status { get; set; } = "";
        public bool CanAssign { get; set; }
    }

    public sealed class OrderProgressBundle
    {
        public List<OpRow> Ops { get; set; } = new();
        public WorkOrderProgressCalculator.Result Progress { get; set; } = new();
    }

    public static async Task<Dictionary<long, OrderProgressBundle>> LoadAsync(
        AppDbContext db,
        long factoryId,
        IReadOnlyList<OrderKey> orders,
        string where = nameof(LoadAsync))
    {
        var result = orders.ToDictionary(
            o => o.Id,
            o => new OrderProgressBundle());
        if (orders.Count == 0)
            return result;

        var orderIds = orders.Select(o => o.Id).ToList();
        var orderMap = orders.ToDictionary(o => o.Id);

        var taskRows = await (
            from t in db.WorkOrderOperations.AsNoTracking()
            where orderIds.Contains(t.WorkOrderId)
            join op in db.Operations.AsNoTracking() on t.OperationId equals op.Id
            select new
            {
                t.Id,
                t.WorkOrderId,
                t.OperationId,
                op.Name,
                t.Seq,
                t.PlanQty,
                t.AssigneeUserId
            }).ToListAsync();

        var tasksByOrder = taskRows.GroupBy(x => x.WorkOrderId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Seq).ToList());

        // 无任务行：按产品当前工艺路线兜底（不落库）
        var needRoute = orders.Where(o => !tasksByOrder.ContainsKey(o.Id)).ToList();
        var routeOpsByOrder = new Dictionary<long, List<(long OperationId, string Name, int Seq, int PlanQty)>>();
        if (needRoute.Count > 0)
        {
            var productIds = needRoute.Select(o => o.ProductId).Distinct().ToList();
            var products = await db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id) && p.FactoryId == factoryId)
                .Select(p => new { p.Id, p.RoutingId })
                .ToListAsync();
            var productRoute = products.ToDictionary(p => p.Id, p => p.RoutingId);
            var routingIds = products.Where(p => p.RoutingId != null).Select(p => p.RoutingId!.Value).Distinct().ToList();
            var steps = routingIds.Count == 0
                ? new List<(long RoutingId, long OperationId, string Name, int Seq)>()
                : (await (
                    from s in db.RoutingSteps.AsNoTracking()
                    where routingIds.Contains(s.RoutingId)
                    join op in db.Operations.AsNoTracking() on s.OperationId equals op.Id
                    select new { s.RoutingId, s.OperationId, op.Name, s.Seq }
                ).ToListAsync()).Select(x => (x.RoutingId, x.OperationId, x.Name, x.Seq)).ToList();
            var stepsByRoute = steps.GroupBy(x => x.RoutingId)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Seq).ToList());

            foreach (var o in needRoute)
            {
                if (!productRoute.TryGetValue(o.ProductId, out var rid) || rid == null)
                    continue;
                if (!stepsByRoute.TryGetValue(rid.Value, out var list) || list.Count == 0)
                    continue;
                routeOpsByOrder[o.Id] = list
                    .Select(s => (s.OperationId, s.Name, s.Seq, o.Qty))
                    .ToList();
            }
        }

        var reportRows = await (
            from r in db.Reports.AsNoTracking()
            where orderIds.Contains(r.OrderId) && r.ReviewStatus != 2
            group r by new { r.OrderId, r.OperationId } into g
            select new
            {
                g.Key.OrderId,
                g.Key.OperationId,
                Done = g.Sum(x => x.GoodQty),
                Defect = g.Sum(x => x.DefectQty)
            }).ToListAsync();
        var reportMap = reportRows.ToDictionary(
            x => (x.OrderId, x.OperationId),
            x => (x.Done, x.Defect));

        var taskIds = taskRows.Select(t => t.Id).ToList();
        var multiAssignees = taskIds.Count == 0
            ? new List<(long TaskId, long UserId)>()
            : (await db.WorkOrderOperationAssignees.AsNoTracking()
                .Where(a => taskIds.Contains(a.WorkOrderOperationId))
                .Select(a => new { a.WorkOrderOperationId, a.UserId })
                .ToListAsync())
                .Select(a => (a.WorkOrderOperationId, a.UserId))
                .ToList();
        var assigneeIds = multiAssignees.Select(a => a.Item2)
            .Concat(taskRows.Where(t => t.AssigneeUserId != null).Select(t => t.AssigneeUserId!.Value))
            .Distinct().ToList();
        var assigneeNames = assigneeIds.Count == 0
            ? new Dictionary<long, string>()
            : await db.Users.AsNoTracking()
                .Where(u => assigneeIds.Contains(u.Id) && u.FactoryId == factoryId)
                .ToDictionaryAsync(u => u.Id, u => u.Name);

        foreach (var order in orders)
        {
            var ops = new List<OpRow>();
            if (tasksByOrder.TryGetValue(order.Id, out var tasks))
            {
                foreach (var t in tasks)
                {
                    reportMap.TryGetValue((order.Id, t.OperationId), out var rd);
                    var ids = multiAssignees.Where(a => a.Item1 == t.Id).Select(a => a.Item2).ToList();
                    if (ids.Count == 0 && t.AssigneeUserId.HasValue)
                        ids.Add(t.AssigneeUserId.Value);
                    var nameStr = string.Join("、", ids.Select(id => assigneeNames.GetValueOrDefault(id, "")).Where(n => n != ""));
                    var row = new OpRow
                    {
                        TaskId = t.Id,
                        OperationId = t.OperationId,
                        OperationName = t.Name,
                        Seq = t.Seq,
                        PlanQty = t.PlanQty,
                        DoneQty = rd.Done,
                        DefectQty = rd.Defect,
                        AssigneeUserId = ids.Count > 0 ? ids[0] : t.AssigneeUserId,
                        AssigneeName = nameStr,
                        IsVirtual = false
                    };
                    row.Status = WorkOrderProgressCalculator.OpStatus(row.PlanQty, row.DoneQty, row.DefectQty, where);
                    row.CanAssign = true;
                    ops.Add(row);
                }
            }
            else if (routeOpsByOrder.TryGetValue(order.Id, out var routeOps))
            {
                foreach (var t in routeOps)
                {
                    reportMap.TryGetValue((order.Id, t.OperationId), out var rd);
                    var row = new OpRow
                    {
                        TaskId = null,
                        OperationId = t.OperationId,
                        OperationName = t.Name,
                        Seq = t.Seq,
                        PlanQty = t.PlanQty,
                        DoneQty = rd.Done,
                        DefectQty = rd.Defect,
                        IsVirtual = true,
                        CanAssign = false
                    };
                    row.Status = WorkOrderProgressCalculator.OpStatus(row.PlanQty, row.DoneQty, row.DefectQty, where);
                    ops.Add(row);
                }
            }

            var calcInputs = ops.Select(o => new WorkOrderProgressCalculator.OpQty
            {
                OperationId = o.OperationId,
                PlanQty = o.PlanQty,
                DoneQty = o.DoneQty,
                DefectQty = o.DefectQty
            }).ToList();
            result[order.Id] = new OrderProgressBundle
            {
                Ops = ops,
                Progress = WorkOrderProgressCalculator.Calc(calcInputs, orderMap[order.Id].Qty, where)
            };
        }

        return result;
    }
}
