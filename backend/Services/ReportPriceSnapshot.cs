using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

/// <summary>报工当刻工价；不存规则可编辑的匹配条件，重算不再查工价表。</summary>
public sealed record ReportPriceSnapshot(long RuleId, byte PriceType, decimal UnitPrice, decimal? DeductPrice)
{
    public ProcessingQuote? Processing { get; init; }
    public static string? Capture(BasePriceRule? rule) => rule == null ? null :
        JsonSerializer.Serialize(new ReportPriceSnapshot(rule.Id, rule.PriceType, rule.UnitPrice, rule.DeductPrice));

    public static PriceRuleMatcher.CalcResult Calculate(ProdReport report, decimal wageCoeff)
    {
        ReportPriceSnapshot? snapshot = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(report.PriceSnapshot))
                snapshot = JsonSerializer.Deserialize<ReportPriceSnapshot>(report.PriceSnapshot);
        }
        catch (JsonException ex)
        {
            throw ThrowHelper.BizUser($"报工 {report.Id} 工价快照损坏，无法计算工资：{ex.Message}");
        }
        if (snapshot == null || (snapshot.RuleId <= 0 && snapshot.Processing == null) || snapshot.PriceType is not (1 or 2 or 3)
            || snapshot.UnitPrice < 0 || snapshot.DeductPrice < 0)
            throw ThrowHelper.BizUser($"报工 {report.Id} 缺少有效工价快照，无法计算工资；请核查原始工价，不可用当前价补算");
        if (snapshot.Processing != null && (snapshot.RuleId >= 0 || snapshot.PriceType != 1 || snapshot.Processing.UnitsPerPriceUnit <= 0
            || snapshot.Processing.UnitPrice != snapshot.UnitPrice || snapshot.Processing.DeductPrice != snapshot.DeductPrice))
            throw ThrowHelper.BizUser($"报工 {report.Id} 加工计价快照无效");
        return PriceRuleMatcher.Calculate(new BasePriceRule
        {
            Id = snapshot.RuleId, PriceType = snapshot.PriceType,
            UnitPrice = snapshot.UnitPrice, DeductPrice = snapshot.DeductPrice
        }, report.GoodQty, report.DefectQty, report.DurationMinutes, wageCoeff, snapshot.Processing?.UnitsPerPriceUnit ?? 1m);
    }
}
