using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ahu.MicrosoftMes.Models;

[Table("sys_wechat_alert_rule")]
public class SysWechatAlertRule
{
    [Column("id")]
    public long Id { get; set; }
    [Column("factory_id")]
    public long FactoryId { get; set; }
    [Column("name")]
    [MaxLength(64)]
    public string Name { get; set; } = "";
    [Column("event_type")]
    [MaxLength(32)]
    public string EventType { get; set; } = "";
    [Column("condition_type")]
    [MaxLength(24)]
    public string ConditionType { get; set; } = "";
    [Column("threshold", TypeName = "decimal(5,2)")]
    public decimal? Threshold { get; set; }
    [Column("target_channel")]
    [MaxLength(16)]
    public string TargetChannel { get; set; } = "session";
    [Column("webhook_key")]
    [MaxLength(200)]
    public string? WebhookKey { get; set; }
    [Column("to_user")]
    [MaxLength(512)]
    public string? ToUser { get; set; }
    [Column("template")]
    [MaxLength(1000)]
    public string Template { get; set; } = "";
    [Column("enabled")]
    public bool Enabled { get; set; }
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}

[Table("sys_wechat_alert_delivery")]
public class SysWechatAlertDelivery
{
    [Column("id")]
    public long Id { get; set; }
    [Column("factory_id")]
    public long FactoryId { get; set; }
    [Column("rule_id")]
    public long RuleId { get; set; }
    [Column("rule_name")]
    [MaxLength(64)]
    public string RuleName { get; set; } = "";
    [Column("event_type")]
    [MaxLength(32)]
    public string EventType { get; set; } = "";
    [Column("event_id")]
    [MaxLength(40)]
    public string EventId { get; set; } = "";
    [Column("occurred_at")]
    public DateTime OccurredAt { get; set; }
    [Column("order_id")]
    public long OrderId { get; set; }
    [Column("report_id")]
    public long? ReportId { get; set; }
    [Column("to_user")]
    [MaxLength(512)]
    public string ToUser { get; set; } = "";
    [Column("content")]
    [MaxLength(1000)]
    public string Content { get; set; } = "";
    [Column("status")]
    [MaxLength(24)]
    public string Status { get; set; } = "";
    [Column("attempts")]
    public int Attempts { get; set; }
    [Column("auto_attempts")]
    public int AutoAttempts { get; set; }
    [Column("next_attempt_at")]
    public DateTime NextAttemptAt { get; set; }
    [Column("lease_id")]
    public Guid? LeaseId { get; set; }
    [Column("lease_until")]
    public DateTime? LeaseUntil { get; set; }
    [Column("sending_started")]
    public bool SendingStarted { get; set; }
    [Column("failed_users")]
    [MaxLength(512)]
    public string? FailedUsers { get; set; }
    [Column("error")]
    [MaxLength(4000)]
    public string? Error { get; set; }
    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }
    [Column("handled_by")]
    public long? HandledBy { get; set; }
    [Column("handled_at")]
    public DateTime? HandledAt { get; set; }
    [Column("handling_note")]
    [MaxLength(256)]
    public string? HandlingNote { get; set; }
}

[Table("sys_wechat_send_attempt")]
public class SysWechatSendAttempt
{
    [Column("id")]
    public long Id { get; set; }
    [Column("factory_id")]
    public long FactoryId { get; set; }
    [Column("request_key")]
    [MaxLength(160)]
    public string RequestKey { get; set; } = "";
    [Column("alert_key")]
    [MaxLength(128)]
    public string AlertKey { get; set; } = "";
    [Column("delivery_id")]
    public long? DeliveryId { get; set; }
    [Column("send_date", TypeName = "date")]
    public DateTime SendDate { get; set; }
    [Column("to_user")]
    [MaxLength(512)]
    public string ToUser { get; set; } = "";
    [Column("outcome")]
    [MaxLength(24)]
    public string Outcome { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    [Column("robot_key_hash")]
    [MaxLength(64)]
    public string? RobotKeyHash { get; set; }
    [Column("charged")]
    public bool Charged { get; set; }
    [Column("failed_users")]
    [MaxLength(512)]
    public string? FailedUsers { get; set; }
    [Column("error")]
    [MaxLength(4000)]
    public string? Error { get; set; }
    [Column("handled_by")]
    public long? HandledBy { get; set; }
    [Column("handling_note")]
    [MaxLength(256)]
    public string? HandlingNote { get; set; }
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}

