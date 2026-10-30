using ahu.MicrosoftMes.Common;
namespace ahu.MicrosoftMes.Services;

/// <summary>仅按已验证快照做金额运算；不查询价格、不匹配规则、不返回未定价零工资。</summary>
public static class WageCalculator
{
    public sealed class CalcResult
    {
        public long? PriceListId { get; init; }
        public byte PriceType { get; init; }
        public decimal UnitPrice { get; init; }
        public int CalcQty { get; init; }
        public decimal Amount { get; init; }
        public decimal DeductAmount { get; init; }
        public decimal WageCoeff { get; init; }
        public decimal WageAmount => Amount - DeductAmount;
        public bool Matched => PriceListId > 0;
    }
    public static CalcResult Calculate(long priceListId, byte priceType, decimal unitPrice, decimal deductPrice,
        int goodQty, int defectQty, int durationMinutes, decimal wageCoeff, decimal unitsPerPriceUnit)
    {
        if (priceListId <= 0 || priceType is not (1 or 2 or 3) || unitPrice < 0 || deductPrice < 0 || unitsPerPriceUnit <= 0 || wageCoeff <= 0 || goodQty < 0 || defectQty < 0 || durationMinutes < 0)
            throw ThrowHelper.BizUser("工资计算失败，请依次检查：该产品工序是否有生效价目表、报工数量和工时是否正常、计件倍数是否已设置。可到「工序价目表」和「报工」中核实。");
        if (priceType != 1 && unitsPerPriceUnit != 1) throw ThrowHelper.BizUser("计时或固定工资的折算值必须为1。");
        var raw = priceType switch { 1 => goodQty * unitPrice / unitsPerPriceUnit * wageCoeff, 2 => durationMinutes / 60m * unitPrice * wageCoeff, _ => unitPrice };
        return new CalcResult { PriceListId = priceListId, PriceType = priceType, UnitPrice = priceType == 1 ? unitPrice / unitsPerPriceUnit : unitPrice,
            CalcQty = priceType == 1 ? goodQty : priceType == 3 ? 1 : 0, Amount = Math.Round(raw, 2, MidpointRounding.AwayFromZero),
            DeductAmount = Math.Round(defectQty * deductPrice / unitsPerPriceUnit, 2, MidpointRounding.AwayFromZero), WageCoeff = priceType == 3 ? 1 : wageCoeff };
    }
}
