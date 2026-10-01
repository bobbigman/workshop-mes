namespace ahu.MicrosoftMes.Models;

// ============ 工资结算（salary_）实体 · docs/21 ============

/// <summary>工资单（按人/周期）</summary>
public class SalaryStatement
{
    public long Id { get; set; }
    public long FactoryId { get; set; }
    public long UserId { get; set; }
    public byte PeriodType { get; set; }            // 1月 2周
    public string PeriodValue { get; set; } = "";   // yyyy-MM / yyyy-Www
    public decimal TotalAmount { get; set; }
    /// <summary>底薪（手工，docs/73）</summary>
    public decimal BaseSalary { get; set; }
    /// <summary>餐补</summary>
    public decimal MealAllowance { get; set; }
    /// <summary>其他补贴</summary>
    public decimal OtherAllowance { get; set; }
    /// <summary>社保个税（正数，系统减）</summary>
    public decimal SocialTax { get; set; }
    /// <summary>其他扣款（正数，系统减）</summary>
    public decimal OtherDeduction { get; set; }
    public byte Status { get; set; }                // 0草稿 1已确认（2已发放预留）
    public DateTime? ConfirmedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>工资明细（逐条对应报工）</summary>
public class SalaryStatementItem
{
    public long Id { get; set; }
    public long StatementId { get; set; }
    public long ReportId { get; set; }
    public long? RuleId { get; set; }
    public int CalcQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public decimal? DeductAmount { get; set; }
}
