namespace ahu.MicrosoftMes.Services;

/// <summary>AI 助手 HTTP 契约 DTO（docs/26 §5）。</summary>
public class AiStatusDto
{
    public bool Enabled { get; set; }
    public bool Configured { get; set; }
    public string? Model { get; set; }
}

public class AiConnectionTestDto
{
    public bool Ok { get; set; }
    public int ElapsedMs { get; set; }
    public string Message { get; set; } = "";
    public string TraceId { get; set; } = "";
}

public class AiMessageRequest
{
    public string? ConversationId { get; set; }
    public string Mode { get; set; } = "";
    public string Message { get; set; } = "";
    public string? SelectedProductCode { get; set; }
    public long? SelectedOperationId { get; set; }
}

public class AiCandidateDto
{
    public string Type { get; set; } = ""; // product | operation
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public long? OperationId { get; set; }
    public string? OperationCode { get; set; }
    public string? OperationName { get; set; }
}

public class AiSourceDto
{
    public string SourceId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Version { get; set; }
    public string? Chapter { get; set; }
    public string? UpdatedAt { get; set; }
    public string Excerpt { get; set; } = "";
    public string? ProductCode { get; set; }
    public string? OperationName { get; set; }
}

public class AiWorkOrderCardDto
{
    public long Id { get; set; }
    public string OrderNo { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int Qty { get; set; }
    public int DoneQty { get; set; }
    public int RemainQty { get; set; }
    public byte Status { get; set; }
    public DateTime? DueDate { get; set; }
}

public class AiWorkOrderPageDto
{
    public List<AiWorkOrderCardDto> List { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public AiWorkOrderFiltersDto Filters { get; set; } = new();
}

public class AiWorkOrderFiltersDto
{
    public string? Keyword { get; set; }
    public string? ProductCode { get; set; }
    public List<byte>? Statuses { get; set; }
    public string? DueFrom { get; set; }
    public string? DueToExclusive { get; set; }
}

public class AiWorkOrderSearchRequest
{
    public string? ConversationId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public AiWorkOrderFiltersDto? Filters { get; set; }
}

public class AiMessageResultDto
{
    public string ConversationId { get; set; } = "";
    public string Answer { get; set; } = "";
    public bool NeedsClarification { get; set; }
    public List<AiCandidateDto> Candidates { get; set; } = new();
    public List<AiSourceDto> Sources { get; set; } = new();
    public AiWorkOrderPageDto? WorkOrders { get; set; }
    public AiReportEvidenceDto? ReportEvidence { get; set; }
    public DateTime QueriedAt { get; set; }
    public List<string> Warnings { get; set; } = new();
    public string TraceId { get; set; } = "";
}

/// <summary>报工证据明细翻页请求（docs/25 §2：不调用模型，每次重新校验会话归属与参数）。</summary>
public class AiReportEvidenceSearchRequest
{
    public string? ConversationId { get; set; }
    public long? WorkOrderId { get; set; }
    public string? OrderNo { get; set; }
    public long? OperationId { get; set; }
    public DateTime? ReportFrom { get; set; }
    public DateTime? ReportToExclusive { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
