using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly IUserService _svc;
    public UserController(IUserService svc) { _svc = svc; }

    [HttpGet]
    public Task<ApiResult<PageResult<UserListDto>>> Query([FromQuery] UserQueryDto q) =>
        _svc.QueryAsync(q, JwtHelper.GetFactoryId(User));

    [HttpPost]
    public Task<ApiResult<object?>> Create([FromBody] UserCreateDto dto) =>
        _svc.CreateAsync(dto, JwtHelper.GetFactoryId(User));

    [HttpPut("{id}")]
    public Task<ApiResult<object?>> Update(long id, [FromBody] UserCreateDto dto) =>
        _svc.UpdateAsync(id, dto, JwtHelper.GetFactoryId(User));

    [HttpDelete("{id}")]
    public Task<ApiResult<object?>> Delete(long id) => _svc.DeleteAsync(id);
}
