using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Authorize]
[Route("api/worker-help")]
public class WorkerHelpController(WorkerHelpService service) : ControllerBase
{
    [HttpGet("status")]
    public Task<ApiResult<object>> Status(CancellationToken ct) =>
        service.StatusAsync(JwtHelper.GetUserId(User), JwtHelper.GetFactoryId(User), ct);

    [HttpPost("messages")]
    public Task<ApiResult<AiMessageResultDto>> Messages(WorkerHelpRequest request, CancellationToken ct) =>
        service.SendAsync(request, JwtHelper.GetUserId(User), JwtHelper.GetFactoryId(User), ct);

    [HttpDelete("conversations/{id}")]
    public Task<ApiResult<object?>> Delete(string id, CancellationToken ct) =>
        service.DeleteAsync(id, JwtHelper.GetUserId(User), JwtHelper.GetFactoryId(User), ct);
}
