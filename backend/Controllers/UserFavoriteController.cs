using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/userfavorite")]
[Authorize]
public class UserFavoriteController : ControllerBase
{
    private readonly IUserFavoriteService _svc;
    public UserFavoriteController(IUserFavoriteService svc) { _svc = svc; }

    [HttpGet]
    public Task<ApiResult<UserFavoriteListDto>> List() =>
        _svc.ListAsync(JwtHelper.GetUserId(User), JwtHelper.GetRole(User));

    [HttpPost("toggle")]
    public Task<ApiResult<UserFavoriteToggleDto>> Toggle([FromBody] UserFavoriteToggleRequest dto) =>
        _svc.ToggleAsync(JwtHelper.GetUserId(User), JwtHelper.GetRole(User), dto.MenuKey);
}
