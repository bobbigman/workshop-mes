using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    public AuthController(IAuthService authService) { _authService = authService; }

    [HttpPost("login")]
    public Task<ApiResult<LoginResultDto>> Login([FromBody] LoginDto dto) =>
        _authService.LoginAsync(dto);

    [HttpPost("login-phone")]
    public Task<ApiResult<LoginResultDto>> LoginByPhone([FromBody] PhoneLoginDto dto) =>
        _authService.LoginByPhoneAsync(dto);

    [Authorize]
    [HttpPost("change-password")]
    public Task<ApiResult<object?>> ChangePassword([FromBody] ChangePasswordDto dto) =>
        _authService.ChangePasswordAsync(dto, JwtHelper.GetUserId(User));

    /// <summary>
    /// 登录页「清缓存并刷新」：仅下发 Clear-Site-Data: "cache"，不触碰 storage/cookies，不读写业务数据。
    /// 须匿名且不带 JWT，避免工人/班组长角色网关拦截。docs/112。
    /// </summary>
    [AllowAnonymous]
    [HttpPost("clear-browser-cache")]
    public ApiResult<ClearBrowserCacheResultDto> ClearBrowserCache()
    {
        // 双引号必须保留；禁止 "storage" / "cookies" / "*"
        Response.Headers["Clear-Site-Data"] = "\"cache\"";
        Response.Headers.CacheControl = "no-store";
        return ApiResult<ClearBrowserCacheResultDto>.Ok(new ClearBrowserCacheResultDto
        {
            HttpCacheClearRequested = true
        });
    }
}

/// <summary>清缓存接口回执：只表示服务端已发出指令，不表示浏览器一定完成清除。</summary>
public class ClearBrowserCacheResultDto
{
    public bool HttpCacheClearRequested { get; set; }
}
