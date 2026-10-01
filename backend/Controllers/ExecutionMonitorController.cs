using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ExecutionMonitorController : ControllerBase
{
    private readonly IExecutionMonitorService _svc;

    public ExecutionMonitorController(IExecutionMonitorService svc) { _svc = svc; }

    [HttpGet]
    public Task<ApiResult<ExecutionMonitorDto>> Get([FromQuery] ExecutionMonitorQueryDto q) =>
        _svc.GetAsync(q, JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User), JwtHelper.GetRole(User));
}
