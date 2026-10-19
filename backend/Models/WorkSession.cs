namespace ahu.MicrosoftMes.Models;

/// <summary>开工与补报留痕；计薪时长以报工记录为准。</summary>
public class ProdWorkSession
{
    public long Id { get; set; }
    public long FactoryId { get; set; }
    public long WorkOrderId { get; set; }
    public long OperationId { get; set; }
    public long UserId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int DurationMinutes { get; set; }
    public byte Status { get; set; }
    public byte Source { get; set; }
    public long? OperatorUserId { get; set; }
    public long? ReportId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}
