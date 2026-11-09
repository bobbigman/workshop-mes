using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OperationController : ControllerBase
{
    private readonly IOperationService _svc;
    public OperationController(IOperationService svc) { _svc = svc; }

    [HttpGet]
    public Task<ApiResult<PageResult<OperationListDto>>> Query([FromQuery] OperationQueryDto q) =>
        _svc.QueryAsync(q, JwtHelper.GetFactoryId(User));

    [HttpGet("{id:long}")]
    public Task<ApiResult<OperationDetailDto>> Get(long id) =>
        _svc.GetAsync(id, JwtHelper.GetFactoryId(User));

    [HttpPost]
    public Task<ApiResult<object?>> Create([FromBody] OperationCreateDto dto) =>
        _svc.CreateAsync(dto, JwtHelper.GetFactoryId(User));

    [HttpPut("{id}")]
    public Task<ApiResult<object?>> Update(long id, [FromBody] OperationCreateDto dto) =>
        _svc.UpdateAsync(id, dto, JwtHelper.GetFactoryId(User));

    [HttpDelete("{id}")]
    public Task<ApiResult<object?>> Delete(long id) => _svc.DeleteAsync(id);

    [HttpPost("{id:long}/forbid")]
    [Authorize(Roles = "1")]
    public Task<ApiResult<object?>> Forbid(long id) =>
        _svc.ForbidAsync(id, JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));

    [HttpPost("{id:long}/unforbid")]
    [Authorize(Roles = "1")]
    public Task<ApiResult<object?>> Unforbid(long id) =>
        _svc.UnforbidAsync(id, JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));

    [HttpPost("{id:long}/audit")]
    [Authorize(Roles = "1")]
    public Task<ApiResult<object?>> Audit(long id) =>
        _svc.AuditAsync(id, JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));

    [HttpPost("{id:long}/unaudit")]
    [Authorize(Roles = "1")]
    public Task<ApiResult<object?>> Unaudit(long id) =>
        _svc.UnauditAsync(id, JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));
}
