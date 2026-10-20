using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using Microsoft.EntityFrameworkCore;

namespace ahu.MicrosoftMes.Services;

public class ReportTaskCandidate
{
    public int Number { get; set; }
    public long TaskId { get; set; }
    public long OrderId { get; set; }
    public long OperationId { get; set; }
    public int Seq { get; set; }
    public string OrderNo { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string OperationName { get; set; } = "";
    public int PlanQty { get; set; }
    public int DoneQty { get; set; }
    public int RemainQty => Math.Max(0, PlanQty - DoneQty);
    public DateTime? DueDate { get; set; }
    public string AssignSource { get; set; } = "available";
}

public class ReportTaskPage
{
    public string Source { get; set; } = "available";
    public int Page { get; set; }
    public bool HasMore { get; set; }
    public List<ReportTaskCandidate> Items { get; set; } = new();
    public Dictionary<string, int> ExcludedReasons { get; set; } = new();
    public string Hint { get; set; } = "";
    public string CandidateListToken { get; set; } = "";
    public int ExpireMinutes { get; set; } = 5;
}

public interface IWecomReportTaskService
{
    Task<ReportTaskPage> QueryAsync(long factoryId, long userId, byte role, string? keyword, int page);
}

/// <summary>候选读取与选择策略分开，查询不写业务数据；ReadAsync 返回稳定排序的批次。</summary>
public interface IReportTaskCandidateSource
{
    Task<List<ReportTaskCandidate>> ReadAsync(long factoryId, long userId, byte role,
        string keyword, long[]? assignedTaskIds, int skip, int take);
}

public class ReportTaskCandidateSource : IReportTaskCandidateSource
{
    private readonly AppDbContext _db;
    public ReportTaskCandidateSource(AppDbContext db) => _db = db;

    public async Task<List<ReportTaskCandidate>> ReadAsync(long factoryId, long userId, byte role,
        string keyword, long[]? assignedTaskIds, int skip, int take)
    {
        var operationIds = await _db.Operations.AsNoTracking().Where(o => o.FactoryId == factoryId)
            .Select(o => o.Id).ToListAsync();
        var allowed = await ReportPermissionHelper.FilterAllowedOperationIdsAsync(_db, userId, role, operationIds);
        if (allowed.Count == 0) return new List<ReportTaskCandidate>();
        return await BuildQuery(factoryId, allowed.ToArray(), keyword, assignedTaskIds)
            .OrderBy(x => x.DueDate == null).ThenBy(x => x.DueDate)
            .ThenBy(x => x.OrderId).ThenBy(x => x.Seq).ThenBy(x => x.OperationId).ThenBy(x => x.TaskId)
            .Skip(skip).Take(take).ToListAsync();
    }

    // 独立查询构造，允许不连接账套检查 SQL Server 翻译；权限 IDs 由上述共用 helper 决定。
    public IQueryable<ReportTaskCandidate> BuildQuery(long factoryId, long[] allowed, string keyword, long[]? assignedTaskIds)
    {
        var orders = _db.WorkOrders.AsNoTracking().Where(o => o.FactoryId == factoryId && (o.Status == 0 || o.Status == 1));
        var products = _db.Products.AsNoTracking().Where(p => p.FactoryId == factoryId);
        var ops = _db.Operations.AsNoTracking().Where(o => o.FactoryId == factoryId && allowed.Contains(o.Id));
        var reports = _db.Reports.AsNoTracking().Where(r => r.FactoryId == factoryId && r.ReviewStatus != 2);
        var tasks = _db.WorkOrderOperations.AsNoTracking();
        var taskRows = from t in tasks
                       join o in orders on t.WorkOrderId equals o.Id
                       join p in products on o.ProductId equals p.Id
                       join op in ops on t.OperationId equals op.Id
                       where assignedTaskIds == null || assignedTaskIds.Contains(t.Id)
                       // 同名多工序不能让旧名称式预览选错；这种工单回退表单。
                       where (from other in tasks
                              join otherOp in _db.Operations on other.OperationId equals otherOp.Id
                              where other.WorkOrderId == o.Id && otherOp.Name == op.Name
                              select other.Id).Count() == 1
                       select new ReportTaskCandidate
                       {
                           TaskId = t.Id, OrderId = o.Id, OrderNo = o.OrderNo, OperationId = op.Id, Seq = t.Seq,
                           ProductName = p.Name, ProductCode = p.Code, OperationName = op.Name,
                           PlanQty = t.PlanQty, DueDate = o.DueDate,
                           DoneQty = reports.Where(r => r.OrderId == o.Id && r.OperationId == op.Id).Sum(r => (int?)r.GoodQty) ?? 0
                       };
        // 兼容原报工服务：工单没有任务快照时读取当前产品路线。
        var routeRows = from o in orders
                        join p in products on o.ProductId equals p.Id
                        join s in _db.RoutingSteps.AsNoTracking() on p.RoutingId equals (long?)s.RoutingId
                        join op in ops on s.OperationId equals op.Id
                        where assignedTaskIds == null && !tasks.Any(t => t.WorkOrderId == o.Id)
                        where (from other in _db.RoutingSteps
                               join otherOp in _db.Operations on other.OperationId equals otherOp.Id
                               where other.RoutingId == s.RoutingId && otherOp.Name == op.Name
                               select other.Id).Count() == 1
                        select new ReportTaskCandidate
                        {
                            TaskId = 0, OrderId = o.Id, OrderNo = o.OrderNo, OperationId = op.Id, Seq = s.Seq,
                            ProductName = p.Name, ProductCode = p.Code, OperationName = op.Name,
                            PlanQty = o.Qty, DueDate = o.DueDate,
                            DoneQty = reports.Where(r => r.OrderId == o.Id && r.OperationId == op.Id).Sum(r => (int?)r.GoodQty) ?? 0
                        };
        return taskRows.Concat(routeRows)
            .Where(x => x.PlanQty > x.DoneQty)
            .Where(x => keyword == "" || x.OrderNo.Contains(keyword) || x.ProductCode.Contains(keyword) || x.ProductName.Contains(keyword));
    }
}

public class WecomReportTaskService : IWecomReportTaskService
{
    public const int PageSize = 5;
    private readonly IAssignService _assign;
    private readonly IReportTaskCandidateSource _source;
    private readonly IReportService _reports;

    public WecomReportTaskService(IAssignService assign, IReportTaskCandidateSource source, IReportService reports)
        => (_assign, _source, _reports) = (assign, source, reports);

    public async Task<ReportTaskPage> QueryAsync(long factoryId, long userId, byte role, string? keyword, int page)
    {
        if (page < 1 || page > 100) throw ThrowHelper.BizUser("页码须为1至100，请缩小产品或工单范围");
        var key = (keyword ?? "").Trim();
        if (key.Length > 100) throw ThrowHelper.BizUser("查询关键词不能超过100字");
        var assigned = await _assign.MyTasksAsync(factoryId, userId);
        if (assigned.Code != 0 || assigned.Data == null)
            throw ThrowHelper.BizUser($"查询本人派工失败：{assigned.Msg}，请稍后重试");
        var sources = assigned.Data.Where(t => t.OrderStatus is 0 or 1 && t.RemainQty > 0)
            .GroupBy(t => t.TaskId).ToDictionary(g => g.Key, g => g.First().AssignSource);
        if (sources.Count > 0)
        {
            var preferred = await ReadPageAsync(factoryId, userId, role, key, page, sources.Keys.ToArray());
            // 不以当前页为空判断没派工：末页仍保留 assigned 来源。
            if (preferred.AnyEligible)
            {
                preferred.Result.Source = "assigned";
                foreach (var item in preferred.Result.Items) item.AssignSource = sources.GetValueOrDefault(item.TaskId, "personal");
                return Finish(preferred.Result);
            }
        }
        var available = await ReadPageAsync(factoryId, userId, role, key, page, null);
        return Finish(available.Result);
    }

    private async Task<(ReportTaskPage Result, bool AnyEligible)> ReadPageAsync(
        long factoryId, long userId, byte role, string keyword, int page, long[]? assignedTaskIds)
    {
        var result = new ReportTaskPage { Page = page };
        var eligible = 0;
        const int batchSize = 50;
        for (var skip = 0; ; skip += batchSize)
        {
            var batch = await _source.ReadAsync(factoryId, userId, role, keyword, assignedTaskIds, skip, batchSize);
            foreach (var item in batch)
            {
                try
                {
                    // 只读校验最小一件，不将工序剩余数当本人的实际产量。
                    await _reports.ValidateSubmitAsync(new ReportDto
                    { OrderId = item.OrderId, OperationId = item.OperationId, GoodQty = 1, Source = 3 }, userId);
                }
                catch (BusinessException ex) when (ex.InnerException == null && !LogRedactor.LooksTechnical(ex.Message))
                {
                    result.ExcludedReasons[ex.Message] = result.ExcludedReasons.GetValueOrDefault(ex.Message) + 1;
                    continue; // 业务不符合条件明确记在返回中；SQL/技术异常不捕获。
                }
                eligible++;
                if (eligible <= (page - 1) * PageSize) continue;
                if (result.Items.Count == PageSize)
                {
                    result.HasMore = true;
                    return (result, true);
                }
                item.Number = result.Items.Count + 1;
                result.Items.Add(item);
            }
            if (batch.Count < batchSize) return (result, eligible > 0);
        }
    }

    private static ReportTaskPage Finish(ReportTaskPage result)
    {
        result.Hint = result.Items.Count > 0
            ? "请选择序号并说明本次完成件数；剩余是工序合计，不是您的产量，实际数量仍须预览校验。"
            : result.Page > 1 ? "本页已无更多任务，请返回前页或换关键词查询。"
            : "暂无符合条件的待报工工序，请换产品/工单关键词或扫码；有前道等限制的工序需先处理。";
        return result;
    }
}
