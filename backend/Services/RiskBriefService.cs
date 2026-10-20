using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using Microsoft.EntityFrameworkCore;

namespace ahu.MicrosoftMes.Services;

public interface IRiskBriefService
{
    Task<RiskBriefDto> QueryAsync(long factoryId, string? period = null, int topN = 3, CancellationToken ct = default);
}

public class RiskBriefDto
{
    public string Period { get; set; } = "today";
    public string AsOfDate { get; set; } = "";
    public string DueThroughDate { get; set; } = "";
    public int Total { get; set; }
    public bool Truncated { get; set; }
    public string Summary { get; set; } = "";
    public string ScopeNote { get; set; } = "当前未结束工单；进度含待复核及已通过报工，不含退回；仅交期和工序滞留，不含不良超标或未处理异常。";
    public List<RiskBriefOrderDto> List { get; set; } = new();
    public string FallbackText { get; set; } = "";
}

public class RiskBriefOrderDto
{
    public long Id { get; set; }
    public string OrderNo { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public bool IsRiskFocus { get; set; }
    public string DueDate { get; set; } = "";
    public int? DaysToDue { get; set; }
    public int DoneQty { get; set; }
    public int RemainQty { get; set; }
    public int? ProgressPercent { get; set; }
    public string? ProgressHint { get; set; }
    public int OverdueDays { get; set; }
    public List<RiskBriefStagnantOpDto> StagnantOperations { get; set; } = new();
    public List<string> Reasons { get; set; } = new();
    public string Suggestion { get; set; } = "";
}

public class RiskBriefStagnantOpDto
{
    public string OperationName { get; set; } = "";
    public int DaysWithoutReport { get; set; }
}

/// <summary>只读风险聚合；MCP 与后续定时简报共用，不触发模型或推送。</summary>
public class RiskBriefService(AppDbContext db, IConfiguration config, TimeProvider clock) : IRiskBriefService
{
    public const int StagnantDays = 3;

    public static string NormalizePeriod(string? period) => (period ?? "today").Trim().ToLowerInvariant() switch
    {
        "today" or "今日" or "今天" => "today",
        "week" or "本周" => "week",
        _ => throw ThrowHelper.BizUser("风险简报仅支持今日或本周")
    };

    public async Task<RiskBriefDto> QueryAsync(long factoryId, string? period = null, int topN = 3, CancellationToken ct = default)
    {
        if (factoryId <= 0) throw ThrowHelper.BizUser("风险简报缺少工厂上下文");
        var key = NormalizePeriod(period);
        if (topN is < 1 or > 10) throw ThrowHelper.BizUser("风险简报条数须为1～10");
        var tz = AiBusinessTime.Resolve(config["Ai:BusinessTimeZone"]);
        var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), tz).DateTime;
        var today = now.Date;
        var dueThrough = key == "week"
            ? today.AddDays(6 - ((int)today.DayOfWeek + 6) % 7)
            : today.AddDays(DueStateHelper.DueWarnDays);

        var orders = await (
            from o in db.WorkOrders.AsNoTracking()
            where o.FactoryId == factoryId && (o.Status == 0 || o.Status == 1)
            join p in db.Products.AsNoTracking().Where(p => p.FactoryId == factoryId) on o.ProductId equals p.Id
            select new { o.Id, o.ProductId, o.Qty, o.OrderNo, o.DueDate, o.CreatedAt,
                ProductCode = p.Code, ProductName = p.Name, p.IsRiskFocus }).ToListAsync(ct);

        var risks = new List<RiskBriefOrderDto>();
        // 控制SQL Server参数数量；不能先取一页工单再筛风险。
        foreach (var batch in orders.Chunk(500))
        {
            ct.ThrowIfCancellationRequested();
            var ids = batch.Select(o => o.Id).ToList();
            var progress = await WorkOrderProgressQuery.LoadAsync(db, factoryId,
                batch.Select(o => new WorkOrderProgressQuery.OrderKey { Id = o.Id, ProductId = o.ProductId, Qty = o.Qty }).ToList(),
                nameof(RiskBriefService), asOf: now);
            var latest = await db.Reports.AsNoTracking()
                .Where(r => r.FactoryId == factoryId && ids.Contains(r.OrderId)
                    && (r.ReviewStatus == 0 || r.ReviewStatus == 1) && r.ReportTime <= now)
                .GroupBy(r => new { r.OrderId, r.OperationId })
                .Select(g => new { g.Key.OrderId, g.Key.OperationId, LastAt = g.Max(r => r.ReportTime) })
                .ToListAsync(ct);
            var lastByOp = latest.ToDictionary(r => (r.OrderId, r.OperationId), r => r.LastAt);
            foreach (var o in batch)
            {
                var bundle = progress[o.Id];
                var row = new RiskBriefOrderDto
                {
                    Id = o.Id, OrderNo = o.OrderNo, ProductCode = o.ProductCode, ProductName = o.ProductName,
                    IsRiskFocus = o.IsRiskFocus, DueDate = AiBusinessTime.FormatDate(o.DueDate),
                    DaysToDue = o.DueDate.HasValue ? (o.DueDate.Value.Date - today).Days : null,
                    DoneQty = bundle.Progress.DoneQty, RemainQty = bundle.Progress.RemainQty,
                    ProgressPercent = bundle.Progress.ProgressPercent, ProgressHint = bundle.Progress.ProgressHint
                };
                if (row.DaysToDue < 0)
                {
                    row.OverdueDays = -row.DaysToDue.Value;
                    row.Reasons.Add($"已超期{row.OverdueDays}天");
                }
                else if (o.DueDate.HasValue && o.DueDate.Value.Date <= dueThrough)
                    row.Reasons.Add($"距交期{row.DaysToDue}天");

                foreach (var op in bundle.Ops.Where(op => op.PlanQty > 0 && op.DoneQty < op.PlanQty))
                {
                    var since = lastByOp.GetValueOrDefault((o.Id, op.OperationId), o.CreatedAt);
                    var days = (today - since.Date).Days;
                    if (days <= StagnantDays) continue;
                    row.StagnantOperations.Add(new() { OperationName = op.OperationName, DaysWithoutReport = days });
                    row.Reasons.Add($"{op.OperationName}已{days}天无有效报工");
                }
                if (row.Reasons.Count == 0) continue;
                row.Suggestion = row.StagnantOperations.Count > 0
                    ? "请核实滞留工序的物料、设备和人员，确认恢复安排"
                    : "请核实剩余工序与交付安排，确认能否按期完成";
                risks.Add(row);
            }
        }

        var list = risks.OrderByDescending(r => r.IsRiskFocus)
            .ThenByDescending(r => r.OverdueDays > 0)
            .ThenByDescending(r => r.StagnantOperations.Count > 0)
            .ThenBy(r => r.DueDate.Length == 0).ThenBy(r => r.DueDate, StringComparer.Ordinal)
            .ThenBy(r => r.Id).Take(topN).ToList();
        var label = key == "week" ? "本周" : "今日";
        var summary = risks.Count == 0 ? $"{label}无超期、临期或滞留风险工单"
            : $"{label}发现{risks.Count}张风险工单，列出前{list.Count}张"
                + (list.Any(r => r.IsRiskFocus) ? "，含你的重点产品，请优先处理" : "");
        return new RiskBriefDto
        {
            Period = key, AsOfDate = today.ToString("yyyy-MM-dd"), DueThroughDate = dueThrough.ToString("yyyy-MM-dd"),
            Total = risks.Count, Truncated = risks.Count > list.Count, Summary = summary, List = list,
            FallbackText = string.Join("\n", new[] { summary }.Concat(list.Select(r =>
                $"{r.OrderNo} {r.ProductName}{(r.IsRiskFocus ? "（你的重点产品）" : "")}：{string.Join("；", r.Reasons)}；建议：{r.Suggestion}")))
        };
    }
}
