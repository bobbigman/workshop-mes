using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ahu.MicrosoftMes.Models;

[Table("base_operation_price_revision")]
public class OperationPriceRevision
{
    [Column("id")] public long Id { get; set; }
    [Column("factory_id")] public long FactoryId { get; set; }
    [Column("price_list_id")] public long PriceListId { get; set; }
    [Column("operation_id")] public long OperationId { get; set; }
    [Column("product_id")] public long? ProductId { get; set; }
    [Column("effective_from")] public DateTime EffectiveFrom { get; set; }
    [Column("effective_to")] public DateTime? EffectiveTo { get; set; }
    [Column("superseded")] public bool Superseded { get; set; }
    [Column("snapshot_json")] public string SnapshotJson { get; set; } = "";
    [Column("fields_json")] public string FieldsJson { get; set; } = "";
    [Column("created_by")] public long CreatedBy { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("base_operation_price_change")]
public class OperationPriceChange
{
    [Column("id")] public long Id { get; set; }
    [Column("factory_id")] public long FactoryId { get; set; }
    [Column("price_list_id")] public long PriceListId { get; set; }
    [Column("request_id")] public Guid RequestId { get; set; }
    [Column("changed_by")] public long ChangedBy { get; set; }
    [Column("reason"),MaxLength(500)] public string Reason { get; set; } = "";
    [Column("effective_from")] public DateTime EffectiveFrom { get; set; }
    [Column("reprice_current_month")] public bool RepriceCurrentMonth { get; set; }
    [Column("request_hash"),MaxLength(64)] public string RequestHash { get; set; } = "";
    [Column("plan_hash"),MaxLength(64)] public string PlanHash { get; set; } = "";
    [Column("before_json")] public string BeforeJson { get; set; } = "";
    [Column("after_json")] public string AfterJson { get; set; } = "";
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}
