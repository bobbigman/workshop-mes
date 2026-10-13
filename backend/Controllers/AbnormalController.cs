using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AbnormalController : ControllerBase
{
    private readonly IAbnormalService _svc;
    public AbnormalController(IAbnormalService svc) { _svc = svc; }

    [HttpPost]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public Task<ApiResult<object?>> Create([FromForm] AbnormalCreateForm form) =>
        _svc.CreateAsync(form, JwtHelper.GetUserId(User), JwtHelper.GetFactoryId(User));

    [HttpGet]
    public Task<ApiResult<PageResult<AbnormalListDto>>> Query([FromQuery] AbnormalQueryDto q) =>
        _svc.QueryAsync(q, JwtHelper.GetFactoryId(User));

    [HttpPost("{id:long}/resolve")]
    public Task<ApiResult<object?>> Resolve(long id, [FromBody] AbnormalResolveDto dto)
    {
        var role = JwtHelper.GetRole(User);
        if (role != 1 && role != 3)
            throw ThrowHelper.BizUser("仅管理员或班组长可处理异常");
        return _svc.ResolveAsync(id, dto ?? new AbnormalResolveDto(), JwtHelper.GetUserId(User), JwtHelper.GetFactoryId(User));
    }
}
