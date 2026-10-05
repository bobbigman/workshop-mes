using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AssignController : ControllerBase
{
    private readonly IAssignService _svc;
    public AssignController(IAssignService svc) { _svc = svc; }

    [HttpGet("my-tasks")]
    public Task<ApiResult<List<AssignTaskDto>>> MyTasks() =>
        _svc.MyTasksAsync(JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));

    /// <summary>可派工人列表：管理员=全厂非管理员；班组长=本组（docs/201）。</summary>
    [HttpGet("workers")]
    public Task<ApiResult<List<AssignWorkerDto>>> Workers() =>
        _svc.WorkersAsync(JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));

    [HttpPut("{taskId:long}")]
    public Task<ApiResult<object?>> Assign(long taskId, [FromBody] AssignDto dto) =>
        _svc.AssignAsync(taskId, dto ?? new AssignDto(), JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));
}
