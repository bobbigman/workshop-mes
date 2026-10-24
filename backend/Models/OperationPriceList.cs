using System.ComponentModel.DataAnnotations.Schema;

namespace ahu.MicrosoftMes.Models;

[Table("base_operation_price_list")]
public class OperationPriceList
{
    public long Id { get; set; }
    public long FactoryId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public long OperationId { get; set; }
    public long? ProductId { get; set; }
    /// <summary>计价方式：1计件 2计时 3单笔固定（一张表只允许一种，首次启用后锁定）。</summary>
    public byte PricingType { get; set; } = 1;
    public string Remark { get; set; } = "";
    public DateTime? ActivatedAt { get; set; }
    public byte Status { get; set; }
    public DateTime EffectiveFrom { get; set; } = DateTime.Today;
    public DateTime? EffectiveTo { get; set; }
    public string PriceUnit { get; set; } = "";
    public long ReportUnitId { get; set; }
    public decimal UnitsPerPriceUnit { get; set; } = 1;
    public string Version { get; set; } = "";
    public string Details { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
