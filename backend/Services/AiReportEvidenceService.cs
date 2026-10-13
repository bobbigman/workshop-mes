using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ahu.MicrosoftMes.Services;

/// <summary>
/// 报工证据查询（只读，docs/25 §4）。
/// 仅投影白名单字段，不把完整报工实体（含工资/单价快照）传给模型；不输出工资/单价/密码等无关字段。
/// </summary>
public interface IAiReportEvidenceService
{
    Task<AiReportEvidenceDto> GetEvidenceAsync(
        long? workOrderId, string? orderNo, long? operationId,
        DateTime? reportFrom, DateTime? reportToExclusive,
        int page, int pageSize, long factoryId, CancellationToken ct);
}

// ============ 返回结构（docs/25 §4） ============

public class AiReportEvidenceDto
{
    public AiEvidenceOrderDto Order { get; set; } = new();
    public AiEvidenceFilterDto Filter { get; set; } = new();
    public AiEvidenceTotalsDto Totals { get; set; } = new();
    public List<AiEvidenceOperationSummaryDto> Summary { get; set; } = new();
    public List<AiEvidenceDetailDto> List { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<AiSourceDto> Sources { get; set; } = new();
    /// <summary>口径提醒：筛选内数量是报工明细汇总，不是最终成品产量。</summary>
    public string Note { get; set; } = "";
}

/// <summary>定位：工单 id/单号、产品编号、查询时间。</summary>
public class AiEvidenceOrderDto
{
    public long Id { get; set; }
    public string OrderNo { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public byte Status { get; set; }
    public DateTime QueriedAt { get; set; }
}

/// <summary>筛选：实际报工时间起止、业务时区、工序、计数口径。</summary>
public class AiEvidenceFilterDto
{
    public DateTime? ReportFrom { get; set; }
    public DateTime? ReportToExclusive { get; set; }
    public string TimeZone { get; set; } = "";
    public long? OperationId { get; set; }
    public string? OperationName { get; set; }
    public string CountRule { get; set; } = "";
}

/// <summary>整单累计：复用原服务的完成数/工序计划数及有效良品数；不受本次报工时间筛选替换。</summary>
public class AiEvidenceTotalsDto
{
    public int Qty { get; set; }
    public int DoneQty { get; set; }
    public List<AiEvidenceTaskDto> Tasks { get; set; } = new();
    public string Rule { get; set; } = "";
}

public class AiEvidenceTaskDto
{
    public long OperationId { get; set; }
    public string OperationName { get; set; } = "";
    public int Seq { get; set; }
    public int PlanQty { get; set; }
    public int DoneQty { get; set; }       // 该工序有效良品合计（待复核+已通过，排除退回）
    public int DefectQty { get; set; }     // 该工序有效不良合计（同上口径）
}

/// <summary>筛选内按工序汇总：待复核/已通过/已退回 良品与不良品、纳入进度和通过口径的数量。</summary>
public class AiEvidenceOperationSummaryDto
{
    public long OperationId { get; set; }
    public string OperationName { get; set; } = "";
    public int PendingGood { get; set; }
    public int ApprovedGood { get; set; }
    public int RejectedGood { get; set; }
    public int PendingDefect { get; set; }
    public int ApprovedDefect { get; set; }
    public int RejectedDefect { get; set; }
    /// <summary>纳入进度（待复核 0 + 已通过 1，排除退回）的良品数。</summary>
    public int ProgressGood { get; set; }
    /// <summary>通过口径（仅已通过 1）的良品数。</summary>
    public int ReportGood { get; set; }
}

/// <summary>明细：报工 id、工序、报工人、报工时间、良品/不良品、复核状态、退回原因、补报批次标记。</summary>
public class AiEvidenceDetailDto
{
    public long Id { get; set; }
    public long OperationId { get; set; }
    public string OperationName { get; set; } = "";
    public string UserName { get; set; } = "";
    public int GoodQty { get; set; }
    public int DefectQty { get; set; }
    public string? DefectName { get; set; }
    public byte ReviewStatus { get; set; }
    public string? RejectReason { get; set; }
    public string? BatchNo { get; set; }
    public DateTime ReportTime { get; set; }
}

public class AiReportEvidenceService : IAiReportEvidenceService
{
    private readonly AppDbContext _db;
    private readonly IWorkOrderService _workOrders;
    private readonly AiOptions _opt;
    private readonly ILogger<AiReportEvidenceService> _logger;

    public AiReportEvidenceService(
        AppDbContext db,
        IWorkOrderService workOrders,
        IOptions<AiOptions> opt,
        ILogger<AiReportEvidenceService> logger)
    {
        _db = db;
        _workOrders = workOrders;
        _opt = opt.Value;
        _logger = logger;
    }

    public async Task<AiReportEvidenceDto> GetEvidenceAsync(
        long? workOrderId, string? orderNo, long? operationId,
        DateTime? reportFrom, DateTime? reportToExclusive,
        int page, int pageSize, long factoryId, CancellationToken ct)
    {
        // —— 参数校验（docs/25 §4：非法时间/起止倒置/非法工序/分页均由服务端拒绝） ——
        if (workOrderId == null && string.IsNullOrWhiteSpace(orderNo))
            throw ThrowHelper.Biz(nameof(GetEvidenceAsync), "请提供 workOrderId 或精确 orderNo");
        if (page < 1)
            throw ThrowHelper.Biz(nameof(GetEvidenceAsync), "page 必须 >= 1");
        if (pageSize < 1 || pageSize > 50)
            throw ThrowHelper.Biz(nameof(GetEvidenceAsync), "pageSize 必须在 1~50 之间");
        if (reportFrom != null && reportToExclusive != null && reportFrom.Value >= reportToExclusive.Value)
            throw ThrowHelper.Biz(nameof(GetEvidenceAsync), "reportFrom 必须早于 reportToExclusive");

        var order = await LocateOrderAsync(workOrderId, orderNo, factoryId, ct);

        // 整单累计：复用原服务口径（与 progress 工具一致）
        var detailRes = await _workOrders.GetAsync(order.Id, factoryId);
        var detail = detailRes.Data
            ?? throw ThrowHelper.Biz(nameof(GetEvidenceAsync), "工单详情读取失败");
        var doneQty = detail.Tasks.Count > 0 ? detail.Tasks.Min(t => t.DoneQty) : 0;

        // 工序必须属于该工单
        string? opName = null;
        if (operationId != null)
        {
            var task = detail.Tasks.FirstOrDefault(t => t.OperationId == operationId.Value)
                ?? throw ThrowHelper.Biz(nameof(GetEvidenceAsync), "该工序不属于此工单");
            opName = task.OperationName;
        }

        // —— 明细 + 汇总 + 总数：单次读取全部匹配行（同一快照），避免并发报工下混用不同读点 ——
        var q = from r in _db.Reports.AsNoTracking()
                where r.FactoryId == factoryId && r.OrderId == order.Id
                join op in _db.Operations.AsNoTracking() on r.OperationId equals op.Id
                join u in _db.Users.AsNoTracking() on r.UserId equals u.Id
                join d in _db.DefectItems.AsNoTracking() on r.DefectId equals d.Id into dg
                from d in dg.DefaultIfEmpty()
                select new
                {
                    r.Id,
                    r.OperationId,
                    OperationName = op.Name,
                    UserName = u.Name,
                    r.GoodQty,
                    r.DefectQty,
                    DefectName = d == null ? null : d.Name,
                    r.ReviewStatus,
                    r.RejectReason,
                    r.BatchNo,
                    r.ReportTime
                };

        if (operationId != null)
            q = q.Where(x => x.OperationId == operationId.Value);
        if (reportFrom != null)
            q = q.Where(x => x.ReportTime >= reportFrom.Value);
        if (reportToExclusive != null)
            q = q.Where(x => x.ReportTime < reportToExclusive.Value);

        var all = await q.ToListAsync(ct);

        var total = all.Count;
        var summary = all
            .GroupBy(x => x.OperationId)
            .Select(g => new AiEvidenceOperationSummaryDto
            {
                OperationId = g.Key,
                OperationName = g.First().OperationName,
                PendingGood = g.Where(x => x.ReviewStatus == 0).Sum(x => x.GoodQty),
                ApprovedGood = g.Where(x => x.ReviewStatus == 1).Sum(x => x.GoodQty),
                RejectedGood = g.Where(x => x.ReviewStatus == 2).Sum(x => x.GoodQty),
                PendingDefect = g.Where(x => x.ReviewStatus == 0).Sum(x => x.DefectQty),
                ApprovedDefect = g.Where(x => x.ReviewStatus == 1).Sum(x => x.DefectQty),
                RejectedDefect = g.Where(x => x.ReviewStatus == 2).Sum(x => x.DefectQty),
                ProgressGood = g.Where(x => x.ReviewStatus != 2).Sum(x => x.GoodQty),
                ReportGood = g.Where(x => x.ReviewStatus == 1).Sum(x => x.GoodQty)
            })
            .OrderBy(x => x.OperationId)
            .ToList();

        var list = all
            .OrderByDescending(x => x.ReportTime)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AiEvidenceDetailDto
            {
                Id = x.Id,
                OperationId = x.OperationId,
                OperationName = x.OperationName,
                UserName = x.UserName,
                GoodQty = x.GoodQty,
                DefectQty = x.DefectQty,
                DefectName = x.DefectName,
                ReviewStatus = x.ReviewStatus,
                RejectReason = x.RejectReason,
                BatchNo = x.BatchNo,
                ReportTime = x.ReportTime
            })
            .ToList();

        var now = DateTime.Now;
        var sourceId = $"evt_{order.Id}";
        var filterDesc = operationId != null
            ? $"工序[{opName}]"
            : (reportFrom != null || reportToExclusive != null
                ? $"时间[{reportFrom:yyyy-MM-dd HH:mm} ~ {reportToExclusive:yyyy-MM-dd HH:mm})"
                : "整单累计");

        var src = new AiSourceDto
        {
            SourceId = sourceId,
            Type = "report_evidence",
            Title = $"报工证据·工单 {order.OrderNo}",
            Excerpt = $"查询 {now:yyyy-MM-dd HH:mm}；{filterDesc}；匹配明细 {total} 条；整单完成数 {doneQty}。"
        };

        _logger.LogInformation("报工证据查询 factory={FactoryId} order={OrderId} op={Op} from={From} to={To} total={Total}",
            factoryId, order.Id, operationId, reportFrom, reportToExclusive, total);

        return new AiReportEvidenceDto
        {
            Order = new AiEvidenceOrderDto
            {
                Id = order.Id,
                OrderNo = order.OrderNo,
                ProductCode = detail.ProductCode,
                ProductName = detail.ProductName,
                Status = order.Status,
                QueriedAt = now
            },
            Filter = new AiEvidenceFilterDto
            {
                ReportFrom = reportFrom,
                ReportToExclusive = reportToExclusive,
                TimeZone = _opt.BusinessTimeZone,
                OperationId = operationId,
                OperationName = opName,
                CountRule = "报工时间 report_time 半开区间 [from, toExclusive)；无日期=整单累计。"
                    + "进度=待复核(0)+已通过(1)排除退回(2)；报表口径=仅已通过(1)。"
            },
            Totals = new AiEvidenceTotalsDto
            {
                Qty = detail.Qty,
                DoneQty = doneQty,
                Tasks = detail.Tasks.Select(t => new AiEvidenceTaskDto
                {
                    OperationId = t.OperationId,
                    OperationName = t.OperationName,
                    Seq = t.Seq,
                    PlanQty = t.PlanQty,
                    DoneQty = t.DoneQty,
                    DefectQty = t.DefectQty
                }).ToList(),
                Rule = "完成数 = 各工序有效良品（待复核+已通过，排除退回）的最小值；无报工工序按 0 参与。"
            },
            Summary = summary,
            List = list,
            Total = total,
            Page = page,
            PageSize = pageSize,
            Sources = new List<AiSourceDto> { src },
            Note = "筛选内数量是报工明细汇总，不是最终成品产量；整单完成数见 Totals。"
        };
    }

    private async Task<ProdWorkOrder> LocateOrderAsync(long? workOrderId, string? orderNo, long factoryId, CancellationToken ct)
    {
        ProdWorkOrder? order = null;
        if (workOrderId != null)
            order = await _db.WorkOrders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == workOrderId.Value && o.FactoryId == factoryId, ct);

        if (!string.IsNullOrWhiteSpace(orderNo))
        {
            var byNo = await _db.WorkOrders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrderNo == orderNo.Trim() && o.FactoryId == factoryId, ct);
            if (order != null && byNo != null && order.Id != byNo.Id)
                throw ThrowHelper.Biz(nameof(LocateOrderAsync), "workOrderId 与 orderNo 指向不同工单");
            if (order == null) order = byNo;
        }

        return order ?? throw ThrowHelper.Biz(nameof(LocateOrderAsync), "未找到工单，请提供 workOrderId 或精确 orderNo");
    }
}
