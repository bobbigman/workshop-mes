using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;

namespace ahu.MicrosoftMes.Services;

public interface IReportStatService
{
    Task<ApiResult<ProductionReportDto>> ProductionAsync(ProductionReportQueryDto query, long factoryId);
    Task<ApiResult<BoardDto>> BoardAsync(long factoryId);
    Task<ApiResult<SkuSummaryDto>> SkuSummaryAsync(SkuSummaryQueryDto query, long factoryId);
    /// <summary>不良品报表三表（docs/207）：分布/汇总/明细。口径：待复核+已通过，不含退回。</summary>
    Task<ApiResult<DefectReportDto>> DefectAsync(DefectReportQueryDto query, long factoryId);
}

public class ProductionReportQueryDto
{
    public string Period { get; set; } = "today";
}

public class ProductionReportDto
{
    public List<ProductionReportItemDto> Items { get; set; } = new();
}

public class ProductionReportItemDto
{
    public string UserName { get; set; } = "";
    public int TotalGood { get; set; }
    public int TotalDefect { get; set; }
    public int ReportCount { get; set; }
}

public class SkuSummaryQueryDto
{
    public string Period { get; set; } = "today";
    public string? ProductCode { get; set; }
    public string? Color { get; set; }
    public string? Spec { get; set; }
}

public class SkuSummaryDto
{
    /// <summary>本厂是否配置了 color 或 spec 自定义字段。</summary>
    public bool Enabled { get; set; }
    public List<SkuSummaryItemDto> Items { get; set; } = new();
}

public class SkuSummaryItemDto
{
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Color { get; set; } = "";
    public string Spec { get; set; } = "";
    public int TotalGood { get; set; }
    public int TotalDefect { get; set; }
    public int ReportCount { get; set; }
}

public class DefectReportQueryDto
{
    public string Period { get; set; } = "today";
    /// <summary>工单号 / 产品编号 / 产品名称关键词（模糊）。</summary>
    public string? Keyword { get; set; }
}

public class DefectReportDto
{
    /// <summary>页上展示的口径说明。</summary>
    public string ReviewScope { get; set; } = "待复核+已通过，不含退回";
    public List<DefectDistributionItemDto> Distribution { get; set; } = new();
    public List<DefectSummaryItemDto> Summary { get; set; } = new();
    public List<DefectDetailItemDto> Details { get; set; } = new();
}

public class DefectDistributionItemDto
{
    public long? DefectId { get; set; }
    public string DefectName { get; set; } = "";
    public int ReportCount { get; set; }
    public int TotalDefect { get; set; }
    /// <summary>占本期内全部不良数的占比（%）。</summary>
    public double SharePercent { get; set; }
    public List<DefectDistributionOpDto> ByOperation { get; set; } = new();
}

public class DefectDistributionOpDto
{
    public long OperationId { get; set; }
    public string OperationName { get; set; } = "";
    public int ReportCount { get; set; }
    public int TotalDefect { get; set; }
}

public class DefectSummaryItemDto
{
    public long OperationId { get; set; }
    public string OperationName { get; set; } = "";
    public long? DefectId { get; set; }
    public string DefectName { get; set; } = "";
    public long UserId { get; set; }
    public string UserName { get; set; } = "";
    public int TotalDefect { get; set; }
    public int TotalGood { get; set; }
    /// <summary>不良率 = 不良/(良品+不良)×100。</summary>
    public double DefectRate { get; set; }
}

public class DefectDetailItemDto
{
    public long Id { get; set; }
    public string OrderNo { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string OperationName { get; set; } = "";
    public string UserName { get; set; } = "";
    public DateTime ReportTime { get; set; }
    public string DefectName { get; set; } = "";
    public int DefectQty { get; set; }
    public int GoodQty { get; set; }
    public int ReviewStatus { get; set; }
}

public class BoardDto
{
    public int TotalOrders { get; set; }
    public int DoingOrders { get; set; }
    public int DoneOrders { get; set; }
    public int TotalGood { get; set; }
    public int TotalDefect { get; set; }
    public double DefectRate { get; set; }
    public int DueWarnCount { get; set; }
    public int DueOverdueCount { get; set; }
    public int OpenAbnormalCount { get; set; }
    public List<BoardAbnormalTickerDto> AbnormalTicker { get; set; } = new();
    public List<BoardOrderProgressDto> Orders { get; set; } = new();
}

public class BoardOrderProgressDto
{
    public string OrderNo { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int Qty { get; set; }
    public int DoneQty { get; set; }
    public int RemainQty { get; set; }
    /// <summary>工序达成进度；与工单列表/执行监控同一口径。</summary>
    public int? ProgressPercent { get; set; }
    public string? ProgressHint { get; set; }
    public DateTime? DueDate { get; set; }
    public string DueState { get; set; } = "normal";
}

public class ReportStatService : IReportStatService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ReportStatService> _logger;
    public ReportStatService(AppDbContext db, ILogger<ReportStatService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ApiResult<ProductionReportDto>> ProductionAsync(ProductionReportQueryDto query, long factoryId)
    {
        var (start, end) = ResolvePeriod(query.Period);

        var items = await (from r in _db.Reports.AsNoTracking()
                           where r.FactoryId == factoryId
                                 && r.ReviewStatus == 1
                                 && r.ReportTime >= start && r.ReportTime < end
                           join u in _db.Users.AsNoTracking() on r.UserId equals u.Id
                           group r by u.Name into g
                           select new ProductionReportItemDto
                           {
                               UserName = g.Key,
                               TotalGood = g.Sum(x => x.GoodQty),
                               TotalDefect = g.Sum(x => x.DefectQty),
                               ReportCount = g.Count()
                           }).ToListAsync();

        return ApiResult<ProductionReportDto>.Ok(new ProductionReportDto { Items = items });
    }

    public async Task<ApiResult<SkuSummaryDto>> SkuSummaryAsync(SkuSummaryQueryDto query, long factoryId)
    {
        var (start, end) = ResolvePeriod(query.Period);
        var productCode = string.IsNullOrWhiteSpace(query.ProductCode) ? null : query.ProductCode.Trim();
        var colorFilter = string.IsNullOrWhiteSpace(query.Color) ? null : query.Color.Trim();
        var specFilter = string.IsNullOrWhiteSpace(query.Spec) ? null : query.Spec.Trim();

        var keyFields = await _db.CustomFields.AsNoTracking()
            .Where(f => f.FactoryId == factoryId
                        && f.Target == "work_order"
                        && f.FieldKey != null
                        && (f.FieldKey == "color" || f.FieldKey == "spec"))
            .Select(f => new { f.Id, f.FieldKey })
            .ToListAsync();

        var colorFieldId = keyFields.FirstOrDefault(f => f.FieldKey == "color")?.Id;
        var specFieldId = keyFields.FirstOrDefault(f => f.FieldKey == "spec")?.Id;

        if (colorFieldId == null && specFieldId == null)
        {
            _logger.LogInformation(
                "SkuSummary factory={FactoryId} period={Period} productCode={ProductCode} color={Color} spec={Spec} enabled=false rows=0",
                factoryId, query.Period, productCode, colorFilter, specFilter);
            return ApiResult<SkuSummaryDto>.Ok(new SkuSummaryDto { Enabled = false });
        }

        var reportRows = await (
            from r in _db.Reports.AsNoTracking()
            where r.FactoryId == factoryId
                  && r.ReviewStatus == 1
                  && r.ReportTime >= start && r.ReportTime < end
            join o in _db.WorkOrders.AsNoTracking() on r.OrderId equals o.Id
            join p in _db.Products.AsNoTracking() on o.ProductId equals p.Id
            select new
            {
                OrderId = o.Id,
                ProductCode = p.Code,
                ProductName = p.Name,
                r.GoodQty,
                r.DefectQty
            }).ToListAsync();

        var orderIds = reportRows.Select(x => x.OrderId).Distinct().ToList();
        var valueFieldIds = new List<long>();
        if (colorFieldId.HasValue) valueFieldIds.Add(colorFieldId.Value);
        if (specFieldId.HasValue) valueFieldIds.Add(specFieldId.Value);

        Dictionary<(long OrderId, long FieldId), string> valueMap;
        if (orderIds.Count == 0 || valueFieldIds.Count == 0)
        {
            valueMap = new Dictionary<(long, long), string>();
        }
        else
        {
            var vals = await _db.CustomFieldValues.AsNoTracking()
                .Where(v => valueFieldIds.Contains(v.FieldId) && orderIds.Contains(v.TargetId))
                .Select(v => new { v.FieldId, v.TargetId, v.Value })
                .ToListAsync();
            valueMap = vals.ToDictionary(v => (v.TargetId, v.FieldId), v => v.Value ?? "");
        }

        string Lookup(long orderId, long? fieldId)
        {
            if (!fieldId.HasValue) return "";
            return valueMap.TryGetValue((orderId, fieldId.Value), out var v) ? v : "";
        }

        var rows = reportRows.Select(x => new
        {
            x.ProductCode,
            x.ProductName,
            Color = Lookup(x.OrderId, colorFieldId),
            Spec = Lookup(x.OrderId, specFieldId),
            x.GoodQty,
            x.DefectQty
        }).AsEnumerable();

        if (productCode != null)
            rows = rows.Where(x => string.Equals(x.ProductCode, productCode, StringComparison.OrdinalIgnoreCase));
        if (colorFilter != null)
            rows = rows.Where(x => string.Equals(x.Color, colorFilter, StringComparison.Ordinal));
        if (specFilter != null)
            rows = rows.Where(x => string.Equals(x.Spec, specFilter, StringComparison.Ordinal));

        var items = rows
            .GroupBy(x => new { x.ProductCode, x.ProductName, x.Color, x.Spec })
            .Select(g => new SkuSummaryItemDto
            {
                ProductCode = g.Key.ProductCode,
                ProductName = g.Key.ProductName,
                Color = g.Key.Color,
                Spec = g.Key.Spec,
                TotalGood = g.Sum(x => x.GoodQty),
                TotalDefect = g.Sum(x => x.DefectQty),
                ReportCount = g.Count()
            })
            .OrderBy(x => x.ProductCode)
            .ThenBy(x => x.Color)
            .ThenBy(x => x.Spec)
            .ToList();

        _logger.LogInformation(
            "SkuSummary factory={FactoryId} period={Period} productCode={ProductCode} color={Color} spec={Spec} enabled=true rows={Rows}",
            factoryId, query.Period, productCode, colorFilter, specFilter, items.Count);

        return ApiResult<SkuSummaryDto>.Ok(new SkuSummaryDto { Enabled = true, Items = items });
    }

    private static (DateTime start, DateTime end) ResolvePeriod(string? period)
    {
        var now = DateTime.Now;
        switch (period)
        {
            case "month":
                {
                    var start = new DateTime(now.Year, now.Month, 1);
                    return (start, start.AddMonths(1));
                }
            case "lastMonth":
                {
                    var start = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
                    var end = new DateTime(now.Year, now.Month, 1);
                    return (start, end);
                }
            default:
                {
                    var start = now.Date;
                    return (start, start.AddDays(1));
                }
        }
    }

    public async Task<ApiResult<DefectReportDto>> DefectAsync(DefectReportQueryDto query, long factoryId)
    {
        var (start, end) = ResolvePeriod(query.Period);
        var keyword = string.IsNullOrWhiteSpace(query.Keyword) ? null : query.Keyword.Trim();

        // 口径：待复核(0)+已通过(1)，不含退回(2)；仅有不良数的报工（docs/207）
        var baseQuery =
            from r in _db.Reports.AsNoTracking()
            where r.FactoryId == factoryId
                  && r.ReviewStatus != 2
                  && r.DefectQty > 0
                  && r.ReportTime >= start && r.ReportTime < end
            join o in _db.WorkOrders.AsNoTracking() on r.OrderId equals o.Id
            join p in _db.Products.AsNoTracking() on o.ProductId equals p.Id
            join op in _db.Operations.AsNoTracking() on r.OperationId equals op.Id
            join u in _db.Users.AsNoTracking() on r.UserId equals u.Id
            join d in _db.DefectItems.AsNoTracking() on r.DefectId equals d.Id into dj
            from d in dj.DefaultIfEmpty()
            select new
            {
                r.Id,
                r.DefectId,
                DefectName = d != null ? d.Name : "(未填原因)",
                r.OperationId,
                OperationName = op.Name,
                r.UserId,
                UserName = u.Name,
                r.GoodQty,
                r.DefectQty,
                r.ReportTime,
                r.ReviewStatus,
                o.OrderNo,
                ProductCode = p.Code,
                ProductName = p.Name
            };

        if (keyword != null)
        {
            baseQuery = baseQuery.Where(x =>
                x.OrderNo.Contains(keyword)
                || x.ProductCode.Contains(keyword)
                || x.ProductName.Contains(keyword));
        }

        var rows = await baseQuery.ToListAsync();

        var totalDefectAll = rows.Sum(x => x.DefectQty);

        var distribution = rows
            .GroupBy(x => new { x.DefectId, x.DefectName })
            .Select(g =>
            {
                var totalDefect = g.Sum(x => x.DefectQty);
                var byOp = g.GroupBy(x => new { x.OperationId, x.OperationName })
                    .Select(og => new DefectDistributionOpDto
                    {
                        OperationId = og.Key.OperationId,
                        OperationName = og.Key.OperationName,
                        ReportCount = og.Count(),
                        TotalDefect = og.Sum(x => x.DefectQty)
                    })
                    .OrderByDescending(x => x.TotalDefect)
                    .ThenBy(x => x.OperationName)
                    .ToList();
                return new DefectDistributionItemDto
                {
                    DefectId = g.Key.DefectId,
                    DefectName = g.Key.DefectName,
                    ReportCount = g.Count(),
                    TotalDefect = totalDefect,
                    SharePercent = totalDefectAll == 0
                        ? 0
                        : Math.Round(totalDefect * 100.0 / totalDefectAll, 2),
                    ByOperation = byOp
                };
            })
            .OrderByDescending(x => x.TotalDefect)
            .ThenBy(x => x.DefectName)
            .ToList();

        var summary = rows
            .GroupBy(x => new { x.OperationId, x.OperationName, x.DefectId, x.DefectName, x.UserId, x.UserName })
            .Select(g =>
            {
                var totalDefect = g.Sum(x => x.DefectQty);
                var totalGood = g.Sum(x => x.GoodQty);
                var denom = totalGood + totalDefect;
                return new DefectSummaryItemDto
                {
                    OperationId = g.Key.OperationId,
                    OperationName = g.Key.OperationName,
                    DefectId = g.Key.DefectId,
                    DefectName = g.Key.DefectName,
                    UserId = g.Key.UserId,
                    UserName = g.Key.UserName,
                    TotalDefect = totalDefect,
                    TotalGood = totalGood,
                    DefectRate = denom == 0 ? 0 : Math.Round(totalDefect * 100.0 / denom, 2)
                };
            })
            .OrderByDescending(x => x.TotalDefect)
            .ThenBy(x => x.OperationName)
            .ThenBy(x => x.DefectName)
            .ThenBy(x => x.UserName)
            .ToList();

        var details = rows
            .OrderByDescending(x => x.ReportTime)
            .ThenByDescending(x => x.Id)
            .Select(x => new DefectDetailItemDto
            {
                Id = x.Id,
                OrderNo = x.OrderNo,
                ProductCode = x.ProductCode,
                ProductName = x.ProductName,
                OperationName = x.OperationName,
                UserName = x.UserName,
                ReportTime = x.ReportTime,
                DefectName = x.DefectName,
                DefectQty = x.DefectQty,
                GoodQty = x.GoodQty,
                ReviewStatus = x.ReviewStatus
            })
            .ToList();

        _logger.LogInformation(
            "DefectReport factory={FactoryId} period={Period} keyword={Keyword} dist={Dist} summary={Summary} detail={Detail}",
            factoryId, query.Period, keyword, distribution.Count, summary.Count, details.Count);

        return ApiResult<DefectReportDto>.Ok(new DefectReportDto
        {
            Distribution = distribution,
            Summary = summary,
            Details = details
        });
    }

    public async Task<ApiResult<BoardDto>> BoardAsync(long factoryId)
    {
        var orders = await _db.WorkOrders.AsNoTracking()
            .Where(o => o.FactoryId == factoryId)
            .Select(o => new { o.Id, o.OrderNo, o.ProductId, o.Qty, o.Status, o.DueDate })
            .ToListAsync();
        var products = await _db.Products.AsNoTracking()
            .Where(p => p.FactoryId == factoryId)
            .ToDictionaryAsync(p => p.Id, p => p.Name);

        var totalGood = await _db.Reports.AsNoTracking()
            .Where(r => r.FactoryId == factoryId && r.ReviewStatus != 2)
            .SumAsync(r => (int?)r.GoodQty) ?? 0;
        var totalDefect = await _db.Reports.AsNoTracking()
            .Where(r => r.FactoryId == factoryId && r.ReviewStatus != 2)
            .SumAsync(r => (int?)r.DefectQty) ?? 0;
        var rate = totalGood + totalDefect == 0 ? 0 : Math.Round(totalDefect * 100.0 / (totalGood + totalDefect), 2);

        var keys = orders.Select(o => new WorkOrderProgressQuery.OrderKey
        {
            Id = o.Id,
            ProductId = o.ProductId,
            Qty = o.Qty
        }).ToList();
        var bundles = await WorkOrderProgressQuery.LoadAsync(_db, factoryId, keys, nameof(BoardAsync));

        var now = DateTime.Now;
        var progress = orders.Select(o =>
        {
            var bundle = bundles[o.Id];
            return new BoardOrderProgressDto
            {
                OrderNo = o.OrderNo,
                ProductName = products.GetValueOrDefault(o.ProductId, ""),
                Qty = o.Qty,
                DoneQty = bundle.Progress.DoneQty,
                RemainQty = bundle.Progress.RemainQty,
                ProgressPercent = bundle.Progress.ProgressPercent,
                ProgressHint = bundle.Progress.ProgressHint,
                DueDate = o.DueDate,
                DueState = DueStateHelper.Calc(o.Status, o.DueDate, now)
            };
        }).ToList();

        var dueWarnCount = progress.Count(p => p.DueState == Common.DueState.Warning);
        var dueOverdueCount = progress.Count(p => p.DueState == Common.DueState.Overdue);

        var openCount = await _db.Abnormals.AsNoTracking()
            .CountAsync(a => a.FactoryId == factoryId && a.Status == 0);
        var openAbnormals = await _db.Abnormals.AsNoTracking()
            .Where(a => a.FactoryId == factoryId && a.Status == 0)
            .OrderByDescending(a => a.ReportedAt)
            .Take(20)
            .ToListAsync();
        var tickerOrderIds = openAbnormals.Where(a => a.WorkOrderId.HasValue).Select(a => a.WorkOrderId!.Value).Distinct().ToList();
        var tickerOrderNos = tickerOrderIds.Count == 0
            ? new Dictionary<long, string>()
            : await _db.WorkOrders.AsNoTracking()
                .Where(o => tickerOrderIds.Contains(o.Id) && o.FactoryId == factoryId)
                .ToDictionaryAsync(o => o.Id, o => o.OrderNo);

        var ticker = openAbnormals.Select(a => new BoardAbnormalTickerDto
        {
            Id = a.Id,
            AbnormalType = a.AbnormalType,
            TypeLabel = AbnormalService.TypeLabel(a.AbnormalType),
            Description = a.Description.Length > 40 ? a.Description[..40] + "…" : a.Description,
            OrderNo = a.WorkOrderId.HasValue ? tickerOrderNos.GetValueOrDefault(a.WorkOrderId.Value) : null,
            ReportedAt = a.ReportedAt
        }).ToList();

        return ApiResult<BoardDto>.Ok(new BoardDto
        {
            TotalOrders = orders.Count,
            DoingOrders = orders.Count(o => o.Status == 1),
            DoneOrders = orders.Count(o => o.Status == 2),
            TotalGood = totalGood,
            TotalDefect = totalDefect,
            DefectRate = rate,
            DueWarnCount = dueWarnCount,
            DueOverdueCount = dueOverdueCount,
            OpenAbnormalCount = openCount,
            AbnormalTicker = ticker,
            Orders = progress
        });
    }
}
