using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IAssignService
{
    Task<ApiResult<List<AssignTaskDto>>> MyTasksAsync(long factoryId, long currentUserId);
    Task<ApiResult<object?>> AssignAsync(long taskId, long? userId, long factoryId, long currentUserId);
}

public class AssignDto
{
    public long? UserId { get; set; }   // 执行人；null=取消派工
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
        // 派给我的工序任务（prod_work_order_operation.assignee_user_id = 当前用户）
        var rows = await (from t in _db.WorkOrderOperations.AsNoTracking()
                          where t.AssigneeUserId == currentUserId
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
                              o.Status
                          }).ToListAsync();

        if (rows.Count == 0)
            return ApiResult<List<AssignTaskDto>>.Ok(new List<AssignTaskDto>());

        // 该批任务的已报良品汇总（进度轨：排除退回 review_status=2）
        var sums = await (from r in _db.Reports.AsNoTracking()
                          where r.FactoryId == factoryId && r.ReviewStatus != 2
                          join t in _db.WorkOrderOperations.AsNoTracking() on r.OrderId equals t.WorkOrderId
                          where t.AssigneeUserId == currentUserId && t.OperationId == r.OperationId
                          group r by t.Id into g
                          select new { TaskId = g.Key, Done = g.Sum(x => x.GoodQty) })
                    .ToListAsync();
        var doneMap = sums.ToDictionary(x => x.TaskId, x => x.Done);

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

    public async Task<ApiResult<object?>> AssignAsync(long taskId, long? userId, long factoryId, long currentUserId)
    {
        var task = await _db.WorkOrderOperations.FirstOrDefaultAsync(t => t.Id == taskId)
            ?? throw ThrowHelper.Biz(nameof(AssignAsync), "工序任务不存在");

        var order = await _db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == task.WorkOrderId && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(AssignAsync), "工单不存在或不属于本厂");

        if (userId.HasValue)
        {
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value && u.FactoryId == factoryId)
                ?? throw ThrowHelper.Biz(nameof(AssignAsync), "执行人不存在或不属于本厂");
        }

        task.AssigneeUserId = userId;
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }
}
