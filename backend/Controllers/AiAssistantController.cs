using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

/// <summary>PC 内置 AI 助手。仅管理员 role=1；工厂取自 JWT。</summary>
[ApiController]
[Authorize]
[Route("api/ai-assistant")]
public class AiAssistantController : ControllerBase
{
    private readonly IAiAssistantService _service;

    public AiAssistantController(IAiAssistantService service) { _service = service; }

    private void EnsureAdmin()
    {
        var role = JwtHelper.GetRole(User);
        if (role != 1)
            throw ThrowHelper.Biz(nameof(EnsureAdmin), "仅管理员可使用 AI 助手");
    }

    [HttpGet("status")]
    public ApiResult<AiStatusDto> Status()
    {
        EnsureAdmin();
        return _service.GetStatus();
    }

    [HttpPost("connection-test")]
    public Task<ApiResult<AiConnectionTestDto>> ConnectionTest(CancellationToken ct)
    {
        EnsureAdmin();
        return _service.ConnectionTestAsync(ct);
    }

    [HttpPost("messages")]
    public Task<ApiResult<AiMessageResultDto>> Messages([FromBody] AiMessageRequest req, CancellationToken ct)
    {
        EnsureAdmin();
        return _service.SendMessageAsync(req, JwtHelper.GetUserId(User), JwtHelper.GetFactoryId(User), ct);
    }

    [HttpDelete("conversations/{id}")]
    public ApiResult<object?> DeleteConversation(string id)
    {
        EnsureAdmin();
        return _service.DeleteConversation(id, JwtHelper.GetUserId(User), JwtHelper.GetFactoryId(User));
    }

    [HttpPost("work-orders/search")]
    public Task<ApiResult<AiWorkOrderPageDto>> SearchWorkOrders([FromBody] AiWorkOrderSearchRequest req, CancellationToken ct)
    {
        EnsureAdmin();
        return _service.SearchWorkOrdersAsync(req, JwtHelper.GetUserId(User), JwtHelper.GetFactoryId(User), ct);
    }

    [HttpPost("report-evidence/search")]
    public Task<ApiResult<AiReportEvidenceDto>> SearchReportEvidence([FromBody] AiReportEvidenceSearchRequest req, CancellationToken ct)
    {
        EnsureAdmin();
        return _service.SearchReportEvidenceAsync(req, JwtHelper.GetUserId(User), JwtHelper.GetFactoryId(User), ct);
    }
}
