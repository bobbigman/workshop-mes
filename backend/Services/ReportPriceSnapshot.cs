using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Models;
namespace ahu.MicrosoftMes.Services;

public sealed record ReportPriceSnapshot(long PriceListId, byte PriceType, decimal UnitPrice, decimal? DeductPrice)
{
    public sealed record CoefficientChange(DateTime ChangedAt,long OperatorId,string Source,decimal? BeforeCoeff,decimal? BeforeAmount,decimal AfterCoeff,decimal AfterAmount);
    public List<CoefficientChange>? CoefficientHistory { get; init; }
    public long? PriceRevisionId { get; init; }
    public decimal? AppliedWageCoeff { get; init; }
    public int SchemaVersion { get; init; } = 2;
    public ProcessingQuote? Processing { get; init; }
    public static string WithCoefficient(ProdReport report,decimal coeff,decimal amount,long actor,string source)
    {
        var snapshot=OperationPricingService.Read<ReportPriceSnapshot>(report.PriceSnapshot!);
        var history=snapshot.CoefficientHistory?.ToList() ?? new();
        if(snapshot.AppliedWageCoeff!=coeff || report.WageAmount!=amount)
            history.Add(new(DateTime.Now,actor,source,snapshot.AppliedWageCoeff,report.WageAmount,coeff,amount));
        return OperationPricingService.Write(snapshot with {AppliedWageCoeff=coeff,CoefficientHistory=history.Count==0?null:history});
    }
    public static WageCalculator.CalcResult Calculate(ProdReport report, decimal wageCoeff)
    {
        ReportPriceSnapshot? snapshot;
        try { snapshot = string.IsNullOrWhiteSpace(report.PriceSnapshot) ? null : JsonSerializer.Deserialize<ReportPriceSnapshot>(report.PriceSnapshot, OperationPricingService.Json); }
        catch (JsonException ex) { throw ThrowHelper.BizUser($"报工{report.Id}价目表快照损坏，无法计算工资：{ex.Message}"); }
        if (snapshot?.SchemaVersion != 2 || snapshot.PriceListId <= 0 || snapshot.Processing == null)
            throw ThrowHelper.BizUser($"报工{report.Id}缺少新版价目表快照，禁止使用当前价补算");
        var quote = snapshot.Processing; OperationPricingService.ValidateQuote(quote);
        if (snapshot.PriceListId != quote.AttributeEvidence!.PriceListId || snapshot.PriceType != quote.PriceType || snapshot.UnitPrice != quote.UnitPrice || snapshot.DeductPrice != quote.DeductPrice || report.FactoryId != quote.FactoryId || report.OperationId != quote.OperationId || (report.ProductId.HasValue && report.ProductId != quote.ProductId))
            throw ThrowHelper.BizUser($"报工{report.Id}快照来源或金额不一致，无法计算工资");
        return WageCalculator.Calculate(snapshot.PriceListId, snapshot.PriceType, snapshot.UnitPrice, snapshot.DeductPrice ?? 0,
            report.GoodQty, report.DefectQty, report.DurationMinutes, wageCoeff, quote.UnitsPerPriceUnit);
    }
}
