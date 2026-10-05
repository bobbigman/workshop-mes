using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WorkerViewController : ControllerBase
{
    private readonly IWorkerViewService _svc;
    public WorkerViewController(IWorkerViewService svc) { _svc = svc; }

    /// <summary>读本厂工人手机视角；工人/班组长只读，管理员可写。</summary>
    [HttpGet]
    public Task<ApiResult<WorkerViewDto>> Get() =>
        _svc.GetAsync(JwtHelper.GetFactoryId(User));

    [HttpPut]
    public Task<ApiResult<object?>> Save([FromBody] WorkerViewDto dto) =>
        _svc.SaveAsync(dto, JwtHelper.GetFactoryId(User), JwtHelper.GetRole(User));
}
