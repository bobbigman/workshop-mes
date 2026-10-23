using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

/// <summary>工价匹配与算额（docs/21）：报工预览与工资结算共用。</summary>
public static class PriceRuleMatcher
{
    public sealed class CalcResult
    {
        public long? RuleId { get; init; }
        public byte PriceType { get; init; }
        public decimal UnitPrice { get; init; }
        public int CalcQty { get; init; }
        public decimal Amount { get; init; }
        public decimal DeductAmount { get; init; }
        /// <summary>本行实际乘进金额的工价系数。计件/计时=用户系数；固定与未匹配=1（不乘）。</summary>
        public decimal WageCoeff { get; init; } = 1m;
        public decimal WageAmount => Amount - DeductAmount;
        public bool Matched => RuleId.HasValue;
    }

    public static BasePriceRule? Match(
        IEnumerable<BasePriceRule> rules,
        long factoryId,
        long? productId,
        long operationId,
        long? departmentId,
        long userId,
        DateTime atTime)
    {
        var candidates = rules
            .Where(r => r.FactoryId == factoryId)
            .Where(r => r.EffectiveFrom <= atTime)
            .Where(r => r.EffectiveTo == null || atTime < r.EffectiveTo)
            .Where(r => r.ProductId == null || r.ProductId == productId)
            .Where(r => r.OperationId == null || r.OperationId == operationId)
            .Where(r => r.DepartmentId == null || r.DepartmentId == departmentId)
            .Where(r => r.UserId == null || r.UserId == userId)
            .Select(r => new
            {
                Rule = r,
                Score = (r.ProductId != null ? 1 : 0)
                      + (r.OperationId != null ? 1 : 0)
                      + (r.DepartmentId != null ? 1 : 0)
                      + (r.UserId != null ? 1 : 0)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Rule.Priority)
            .ThenBy(x => x.Rule.Id)
            .FirstOrDefault();

        return candidates?.Rule;
    }

    /// <param name="wageCoeff">人级工价系数。1 与不传相同。计件/计时乘入金额后再四舍五入到分；固定不乘；扣款始终不乘。</param>
    public static CalcResult Calculate(BasePriceRule? rule, int goodQty, int defectQty, int durationMinutes, decimal wageCoeff = 1m, decimal unitsPerPriceUnit = 1m)
    {
        if (wageCoeff <= 0m) wageCoeff = 1m;

        if (rule == null)
        {
            return new CalcResult
            {
                RuleId = null,
                UnitPrice = 0,
                CalcQty = 0,
                Amount = 0,
                DeductAmount = 0,
                WageCoeff = 1m
            };
        }

        if (unitsPerPriceUnit <= 0) throw ahu.MicrosoftMes.Common.ThrowHelper.BizUser("计价单位换算必须大于0");
        var deduct = defectQty * (rule.DeductPrice ?? 0m) / unitsPerPriceUnit;
        decimal amount;
        int calcQty;
        decimal applied = 1m;
        switch (rule.PriceType)
        {
            case 2: // 计时
                var hourly = durationMinutes / 60m * rule.UnitPrice;
                if (wageCoeff != 1m) hourly *= wageCoeff;
                amount = Math.Round(hourly, 2, MidpointRounding.AwayFromZero);
                calcQty = 0;
                applied = wageCoeff;
                break;
            case 3: // 固定：月薪不乘系数
                amount = rule.UnitPrice;
                calcQty = 1;
                applied = 1m;
                break;
            default: // 计件
                var piece = goodQty * rule.UnitPrice / unitsPerPriceUnit;
                if (wageCoeff != 1m) piece *= wageCoeff;
                amount = Math.Round(piece, 2, MidpointRounding.AwayFromZero);
                calcQty = goodQty;
                applied = wageCoeff;
                break;
        }

        return new CalcResult
        {
            RuleId = rule.Id,
            PriceType = rule.PriceType,
            UnitPrice = rule.UnitPrice / unitsPerPriceUnit,
            CalcQty = calcQty,
            Amount = amount,
            DeductAmount = Math.Round(deduct, 2, MidpointRounding.AwayFromZero),
            WageCoeff = applied
        };
    }
}
