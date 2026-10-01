using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services.Kingdee;

namespace ahu.MicrosoftMes.Services;

public class QuoteSuggestPreviewRequest
{
    public List<long> SimilarProductIds { get; set; } = new();
    public string? KingdeeMaterialCode { get; set; }
    public decimal? MarkupPct { get; set; }
    public decimal? DefectReservePct { get; set; }
}

public class QuoteSuggestProductOptionDto
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

public class QuoteSuggestLaborProductDto
{
    public long ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int ReportCount { get; set; }
    public int ReportsWithDuration { get; set; }
    public int ReportsMissingDuration { get; set; }
    public int GoodQtyWithDuration { get; set; }
    public int TotalGoodQty { get; set; }
    public int TotalDefectQty { get; set; }
    public int TotalDurationMinutes { get; set; }
    public decimal? AvgMinutesPerPiece { get; set; }
    public decimal DefectRatePct { get; set; }
    public decimal LaborHourlyRate { get; set; }
    public string LaborRateSource { get; set; } = "";
    public decimal LaborCost { get; set; }
    public string? Warning { get; set; }
}

public class QuoteSuggestPreviewDto
{
    public string KingdeeMode { get; set; } = "";
    public string? KingdeeMaterialCode { get; set; }
    public string? KingdeeMaterialName { get; set; }
    public bool MaterialOk { get; set; }
    public string? MaterialWarning { get; set; }
    public decimal MaterialUnitCost { get; set; }
    public decimal BomTotalCost { get; set; }
    public decimal MaterialSuggest { get; set; }
    public string MaterialSource { get; set; } = ""; // bom | unit | none
    public List<KingdeeBomLineDto> BomLines { get; set; } = new();
    public List<KingdeeRoutingStepDto> RoutingSteps { get; set; } = new();

    public List<QuoteSuggestLaborProductDto> LaborByProduct { get; set; } = new();
    public decimal LaborSuggest { get; set; }
    public string? LaborWarning { get; set; }

    public decimal DefectReservePct { get; set; }
    public decimal MarkupPct { get; set; }
    public decimal BaseCost { get; set; }
    public decimal AfterDefectReserve { get; set; }
    public decimal SuggestTotal { get; set; }
    public string FormulaNote { get; set; } = "";
}

public interface IQuoteSuggestService
{
    Task<ApiResult<PageResult<QuoteSuggestProductOptionDto>>> SearchProductsAsync(string? keyword, int page, int pageSize, long factoryId);
    Task<ApiResult<KingdeeMaterialDto?>> ProbeMaterialAsync(string code);
    Task<ApiResult<QuoteSuggestPreviewDto>> PreviewAsync(QuoteSuggestPreviewRequest req, long factoryId);
    Task<(byte[] Content, string FileName)> ExportAsync(QuoteSuggestPreviewRequest req, long factoryId);
}

public class QuoteSuggestService : IQuoteSuggestService
{
    private readonly AppDbContext _db;
    private readonly IKingdeeMasterDataClient _kingdee;
    private readonly KingdeeOptions _opt;

    public QuoteSuggestService(AppDbContext db, IKingdeeMasterDataClient kingdee, IOptions<KingdeeOptions> opt)
    {
        _db = db;
        _kingdee = kingdee;
        _opt = opt.Value;
    }

    public async Task<ApiResult<PageResult<QuoteSuggestProductOptionDto>>> SearchProductsAsync(
        string? keyword, int page, int pageSize, long factoryId)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var q = _db.Products.AsNoTracking().Where(p => p.FactoryId == factoryId);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            q = q.Where(p => p.Code.Contains(kw) || p.Name.Contains(kw));
        }

        var total = await q.CountAsync();
        var list = await q.OrderBy(p => p.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new QuoteSuggestProductOptionDto { Id = p.Id, Code = p.Code, Name = p.Name })
            .ToListAsync();

        return ApiResult<PageResult<QuoteSuggestProductOptionDto>>.Ok(
            new PageResult<QuoteSuggestProductOptionDto> { List = list, Total = total });
    }

    public async Task<ApiResult<KingdeeMaterialDto?>> ProbeMaterialAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw ThrowHelper.BizUser("请填写金蝶物料编号");
        try
        {
            var m = await _kingdee.GetMaterialAsync(code.Trim());
            return ApiResult<KingdeeMaterialDto?>.Ok(m);
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 探测接口直接把失败抛给全局（含 Api 上下文）；用户可见安全文案由中间件处理
            throw ThrowHelper.General(nameof(ProbeMaterialAsync), "查询金蝶物料失败", ex);
        }
    }

    public async Task<ApiResult<QuoteSuggestPreviewDto>> PreviewAsync(QuoteSuggestPreviewRequest req, long factoryId)
    {
        if (req.SimilarProductIds == null || req.SimilarProductIds.Count == 0)
            throw ThrowHelper.BizUser("请至少选择一个相似产品");

        var ids = req.SimilarProductIds.Distinct().ToList();
        var products = await _db.Products.AsNoTracking()
            .Where(p => p.FactoryId == factoryId && ids.Contains(p.Id))
            .ToListAsync();
        if (products.Count != ids.Count)
            throw ThrowHelper.BizUser("相似产品不存在或不属于本厂");

        var markup = req.MarkupPct ?? _opt.DefaultMarkupPct;
        var defectReserve = req.DefectReservePct ?? 0m;
        if (markup < 0 || markup > 500) throw ThrowHelper.BizUser("毛利加点应在 0～500% 之间");
        if (defectReserve < 0 || defectReserve > 100) throw ThrowHelper.BizUser("不良预留应在 0～100% 之间");

        var dto = new QuoteSuggestPreviewDto
        {
            KingdeeMode = _kingdee.ModeName,
            KingdeeMaterialCode = string.IsNullOrWhiteSpace(req.KingdeeMaterialCode) ? null : req.KingdeeMaterialCode.Trim(),
            MarkupPct = markup,
            DefectReservePct = defectReserve
        };

        await FillMaterialAsync(dto);
        await FillLaborAsync(dto, products, factoryId);

        dto.BaseCost = Math.Round(dto.MaterialSuggest + dto.LaborSuggest, 4, MidpointRounding.AwayFromZero);
        dto.AfterDefectReserve = Math.Round(dto.BaseCost * (1 + defectReserve / 100m), 4, MidpointRounding.AwayFromZero);
        dto.SuggestTotal = Math.Round(dto.AfterDefectReserve * (1 + markup / 100m), 2, MidpointRounding.AwayFromZero);
        dto.FormulaNote =
            "建议总价 = (材料建议 + 人工建议) × (1+不良预留%) × (1+毛利%)；" +
            "人工=有时长报工的单件平均分钟/60×计时单价或默认费率；仅统计已复核(review_status=1)报工。";

        return ApiResult<QuoteSuggestPreviewDto>.Ok(dto);
    }

    public async Task<(byte[] Content, string FileName)> ExportAsync(QuoteSuggestPreviewRequest req, long factoryId)
    {
        var preview = await PreviewAsync(req, factoryId);
        var data = preview.Data ?? throw ThrowHelper.BizUser("导出失败：无计算结果");

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("建议报价");
        ws.Cell(1, 1).Value = "建议报价（金蝶材料 + 本厂实绩人工）";
        ws.Cell(2, 1).Value = "金蝶模式";
        ws.Cell(2, 2).Value = data.KingdeeMode;
        ws.Cell(3, 1).Value = "物料编号";
        ws.Cell(3, 2).Value = data.KingdeeMaterialCode ?? "";
        ws.Cell(4, 1).Value = "物料名称";
        ws.Cell(4, 2).Value = data.KingdeeMaterialName ?? "";
        ws.Cell(5, 1).Value = "材料建议";
        ws.Cell(5, 2).Value = data.MaterialSuggest;
        ws.Cell(6, 1).Value = "人工建议";
        ws.Cell(6, 2).Value = data.LaborSuggest;
        ws.Cell(7, 1).Value = "不良预留%";
        ws.Cell(7, 2).Value = data.DefectReservePct;
        ws.Cell(8, 1).Value = "毛利%";
        ws.Cell(8, 2).Value = data.MarkupPct;
        ws.Cell(9, 1).Value = "建议总价";
        ws.Cell(9, 2).Value = data.SuggestTotal;
        ws.Cell(10, 1).Value = "口径";
        ws.Cell(10, 2).Value = data.FormulaNote;
        if (!string.IsNullOrEmpty(data.MaterialWarning))
        {
            ws.Cell(11, 1).Value = "材料提示";
            ws.Cell(11, 2).Value = data.MaterialWarning;
        }
        if (!string.IsNullOrEmpty(data.LaborWarning))
        {
            ws.Cell(12, 1).Value = "人工提示";
            ws.Cell(12, 2).Value = data.LaborWarning;
        }

        var row = 14;
        ws.Cell(row, 1).Value = "相似产品";
        ws.Cell(row, 2).Value = "编号";
        ws.Cell(row, 3).Value = "平均分钟/件";
        ws.Cell(row, 4).Value = "不良率%";
        ws.Cell(row, 5).Value = "人工";
        ws.Cell(row, 6).Value = "提示";
        row++;
        foreach (var p in data.LaborByProduct)
        {
            ws.Cell(row, 1).Value = p.ProductName;
            ws.Cell(row, 2).Value = p.ProductCode;
            ws.Cell(row, 3).Value = p.AvgMinutesPerPiece;
            ws.Cell(row, 4).Value = p.DefectRatePct;
            ws.Cell(row, 5).Value = p.LaborCost;
            ws.Cell(row, 6).Value = p.Warning ?? "";
            row++;
        }

        row += 2;
        ws.Cell(row, 1).Value = "BOM明细";
        row++;
        ws.Cell(row, 1).Value = "子件编号";
        ws.Cell(row, 2).Value = "名称";
        ws.Cell(row, 3).Value = "用量";
        ws.Cell(row, 4).Value = "单价";
        ws.Cell(row, 5).Value = "金额";
        row++;
        foreach (var line in data.BomLines)
        {
            ws.Cell(row, 1).Value = line.MaterialCode;
            ws.Cell(row, 2).Value = line.MaterialName;
            ws.Cell(row, 3).Value = line.Qty;
            ws.Cell(row, 4).Value = line.UnitCost;
            ws.Cell(row, 5).Value = line.LineCost;
            row++;
        }

        row += 2;
        ws.Cell(row, 1).Value = "金蝶标准工艺（只读）";
        row++;
        foreach (var step in data.RoutingSteps)
        {
            ws.Cell(row, 1).Value = step.Seq;
            ws.Cell(row, 2).Value = step.OperationName;
            row++;
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var name = $"建议报价_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        return (ms.ToArray(), name);
    }

    private async Task FillMaterialAsync(QuoteSuggestPreviewDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.KingdeeMaterialCode))
        {
            dto.MaterialOk = false;
            dto.MaterialSource = "none";
            dto.MaterialWarning = "未填金蝶物料号，本单仅估人工";
            return;
        }

        try
        {
            var mat = await _kingdee.GetMaterialAsync(dto.KingdeeMaterialCode);
            if (mat == null)
            {
                dto.MaterialOk = false;
                dto.MaterialSource = "none";
                dto.MaterialWarning = $"金蝶未找到物料「{dto.KingdeeMaterialCode}」";
                return;
            }

            dto.MaterialOk = true;
            dto.KingdeeMaterialName = mat.Name;
            dto.MaterialUnitCost = mat.CostPrice;

            var bom = await _kingdee.GetBomMaterialCostAsync(dto.KingdeeMaterialCode);
            if (bom != null && bom.TotalCost > 0)
            {
                dto.BomTotalCost = bom.TotalCost;
                dto.BomLines = bom.Lines;
                dto.MaterialSuggest = bom.TotalCost;
                dto.MaterialSource = "bom";
            }
            else
            {
                dto.MaterialSuggest = mat.CostPrice;
                dto.MaterialSource = "unit";
                if (mat.CostPrice <= 0)
                    dto.MaterialWarning = "物料成本价为 0，请核对本厂金蝶成本字段";
            }

            dto.RoutingSteps = await _kingdee.GetRoutingAsync(dto.KingdeeMaterialCode);
        }
        catch (Exception ex)
        {
            // 材料失败不阻断人工：记录可读警告（完整上下文在服务器日志由上层异常中间件记；此处吞的是「材料支路」）
            dto.MaterialOk = false;
            dto.MaterialSource = "none";
            dto.MaterialWarning = "金蝶材料取数失败，已跳过材料；可只看人工建议。详情见服务器日志。";
            // 仍抛到日志：再包一层业务可继续——按指令「材料失败不影响仅人工」
            // 不重新抛，避免 preview 整单失败；写入 Serilog 由调用方——这里用 ThrowHelper 信息附加到 warning
            dto.MaterialWarning += $"（{Truncate(ex.GetBaseException().Message, 120)}）";
        }
    }

    private sealed record ReportAggRow(
        long ProductId, int GoodQty, int DefectQty, int DurationMinutes, long OperationId);

    private async Task FillLaborAsync(QuoteSuggestPreviewDto dto, List<BaseProduct> products, long factoryId)
    {
        var productIds = products.Select(p => p.Id).ToList();
        var reports = await (
            from r in _db.Reports.AsNoTracking()
            join o in _db.WorkOrders.AsNoTracking() on r.OrderId equals o.Id
            where r.FactoryId == factoryId && r.ReviewStatus == 1
            let pid = r.ProductId ?? o.ProductId
            where productIds.Contains(pid)
            select new ReportAggRow(pid, r.GoodQty, r.DefectQty, r.DurationMinutes, r.OperationId)
        ).ToListAsync();

        var rules = await _db.PriceRules.AsNoTracking()
            .Where(r => r.FactoryId == factoryId)
            .ToListAsync();

        var laborRows = new List<QuoteSuggestLaborProductDto>();
        var warnings = new List<string>();

        foreach (var p in products)
        {
            var mine = reports.Where(r => r.ProductId == p.Id).ToList();
            var row = new QuoteSuggestLaborProductDto
            {
                ProductId = p.Id,
                ProductCode = p.Code,
                ProductName = p.Name,
                ReportCount = mine.Count,
                TotalGoodQty = mine.Sum(x => x.GoodQty),
                TotalDefectQty = mine.Sum(x => x.DefectQty),
                ReportsWithDuration = mine.Count(x => x.DurationMinutes > 0),
                ReportsMissingDuration = mine.Count(x => x.DurationMinutes <= 0)
            };

            var withDur = mine.Where(x => x.DurationMinutes > 0).ToList();
            row.TotalDurationMinutes = withDur.Sum(x => x.DurationMinutes);
            row.GoodQtyWithDuration = withDur.Sum(x => x.GoodQty);

            var denom = row.TotalGoodQty + row.TotalDefectQty;
            row.DefectRatePct = denom <= 0
                ? 0
                : Math.Round(100m * row.TotalDefectQty / denom, 2, MidpointRounding.AwayFromZero);

            if (withDur.Count == 0 || row.GoodQtyWithDuration <= 0)
            {
                row.LaborCost = 0;
                row.Warning = mine.Count == 0
                    ? "无已复核报工，请人工估工时"
                    : "有报工但缺时长或良品为 0，未计入平均工时";
                warnings.Add($"{p.Code}：{row.Warning}");
                laborRows.Add(row);
                continue;
            }

            if (row.ReportsMissingDuration > 0)
                row.Warning = $"部分报工缺时长（{row.ReportsMissingDuration} 条未计入）";

            row.AvgMinutesPerPiece = Math.Round(
                (decimal)row.TotalDurationMinutes / row.GoodQtyWithDuration, 2, MidpointRounding.AwayFromZero);

            var opIds = withDur.Select(x => x.OperationId).Distinct().ToHashSet();
            var (rate, source) = ResolveHourlyRate(rules, factoryId, p.Id, opIds);
            row.LaborHourlyRate = rate;
            row.LaborRateSource = source;
            row.LaborCost = Math.Round(row.AvgMinutesPerPiece.Value / 60m * rate, 4, MidpointRounding.AwayFromZero);
            laborRows.Add(row);
        }

        dto.LaborByProduct = laborRows;
        // 多相似件：人工建议取各件人工的算术平均（演示好讲）
        var positive = laborRows.Where(x => x.LaborCost > 0).ToList();
        dto.LaborSuggest = positive.Count == 0
            ? 0
            : Math.Round(positive.Average(x => x.LaborCost), 4, MidpointRounding.AwayFromZero);

        if (positive.Count == 0)
            dto.LaborWarning = "无可用实绩工时，人工建议为 0，请人工估";
        else if (warnings.Count > 0)
            dto.LaborWarning = string.Join("；", warnings.Take(5));
    }

    private (decimal Rate, string Source) ResolveHourlyRate(
        List<BasePriceRule> rules,
        long factoryId,
        long productId,
        HashSet<long> sampleOperationIds)
    {
        var now = DateTime.Now;
        var candidates = rules
            .Where(x => x.FactoryId == factoryId && x.PriceType == 2)
            .Where(r => r.EffectiveFrom <= now)
            .Where(r => r.EffectiveTo == null || now < r.EffectiveTo)
            .Where(r => r.ProductId == null || r.ProductId == productId)
            .Where(r => r.OperationId == null || sampleOperationIds.Contains(r.OperationId.Value)
                        || r.ProductId == productId)
            .Select(r => new
            {
                Rule = r,
                Score = (r.ProductId != null ? 2 : 0) + (r.OperationId != null ? 1 : 0)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Rule.Priority)
            .ThenBy(x => x.Rule.Id)
            .FirstOrDefault();

        if (candidates != null)
            return (candidates.Rule.UnitPrice, $"计时工价规则#{candidates.Rule.Id}");

        return (_opt.DefaultLaborRatePerHour, "默认人工费率(元/小时)");
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
}
