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

    public static CalcResult Calculate(BasePriceRule? rule, int goodQty, int defectQty, int durationMinutes)
    {
        if (rule == null)
        {
            return new CalcResult
            {
                RuleId = null,
                UnitPrice = 0,
                CalcQty = 0,
                Amount = 0,
                DeductAmount = 0
            };
        }

        var deduct = defectQty * (rule.DeductPrice ?? 0m);
        decimal amount;
        int calcQty;
        switch (rule.PriceType)
        {
            case 2: // 计时
                amount = Math.Round(durationMinutes / 60m * rule.UnitPrice, 2, MidpointRounding.AwayFromZero);
                calcQty = 0;
                break;
            case 3: // 固定
                amount = rule.UnitPrice;
                calcQty = 1;
                break;
            default: // 计件
                amount = Math.Round(goodQty * rule.UnitPrice, 2, MidpointRounding.AwayFromZero);
                calcQty = goodQty;
                break;
        }

        return new CalcResult
        {
            RuleId = rule.Id,
            PriceType = rule.PriceType,
            UnitPrice = rule.UnitPrice,
            CalcQty = calcQty,
            Amount = amount,
            DeductAmount = Math.Round(deduct, 2, MidpointRounding.AwayFromZero)
        };
    }
}
