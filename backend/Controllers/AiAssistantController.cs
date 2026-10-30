using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

/// <summary>PC 内置 AI 助手。全员登录用户可用（电脑端页面仍按角色在前端拦截）；工厂取自 JWT。</summary>
[ApiController]
[TypeFilter(typeof(CurrentAdminFilter))]
[Authorize]
[Route("api/ai-assistant")]
public class AiAssistantController : ControllerBase
{
    private readonly IAiAssistantService _service;

    public AiAssistantController(IAiAssistantService service) { _service = service; }

    [HttpGet("status")]
    public ApiResult<AiStatusDto> Status()
    {
        return _service.GetStatus();
    }

    [HttpPost("connection-test")]
    public Task<ApiResult<AiConnectionTestDto>> ConnectionTest(CancellationToken ct)
    {
        return _service.ConnectionTestAsync(ct);
    }

    [HttpPost("messages")]
    public Task<ApiResult<AiMessageResultDto>> Messages([FromBody] AiMessageRequest req, CancellationToken ct)
    {
        return _service.SendMessageAsync(req, JwtHelper.GetUserId(User), JwtHelper.GetFactoryId(User), ct);
    }

    [HttpDelete("conversations/{id}")]
    public ApiResult<object?> DeleteConversation(string id)
    {
        return _service.DeleteConversation(id, JwtHelper.GetUserId(User), JwtHelper.GetFactoryId(User));
    }

    [HttpPost("work-orders/search")]
    public Task<ApiResult<AiWorkOrderPageDto>> SearchWorkOrders([FromBody] AiWorkOrderSearchRequest req, CancellationToken ct)
    {
        return _service.SearchWorkOrdersAsync(req, JwtHelper.GetUserId(User), JwtHelper.GetFactoryId(User), ct);
    }

    [HttpPost("report-evidence/search")]
    public Task<ApiResult<AiReportEvidenceDto>> SearchReportEvidence([FromBody] AiReportEvidenceSearchRequest req, CancellationToken ct)
    {
        return _service.SearchReportEvidenceAsync(req, JwtHelper.GetUserId(User), JwtHelper.GetFactoryId(User), ct);
    }
}
