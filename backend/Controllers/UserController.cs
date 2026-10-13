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

    /// <summary>停用/启用（docs/219）；不踢已发 JWT</summary>
    [HttpPut("{id}/status")]
    public Task<ApiResult<object?>> SetStatus(long id, [FromBody] UserStatusDto dto) =>
        _svc.SetStatusAsync(id, dto.Status, JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));

    /// <summary>获取或首次生成个人免密二维码（docs/150）</summary>
    [HttpGet("{id}/login-qr")]
    public Task<ApiResult<UserLoginQrDto>> GetLoginQr(long id) =>
        _svc.GetOrCreateLoginQrAsync(id, JwtHelper.GetFactoryId(User));

    /// <summary>重新生成个人免密码，旧码立刻失效</summary>
    [HttpPost("{id}/login-qr/regenerate")]
    public Task<ApiResult<UserLoginQrDto>> RegenerateLoginQr(long id) =>
        _svc.RegenerateLoginQrAsync(id, JwtHelper.GetFactoryId(User));
}
