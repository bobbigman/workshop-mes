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

    /// <summary>上报时可选的处理人：本厂启用用户，只回 id 与姓名（docs/228）。</summary>
    [HttpGet("handlers")]
    public Task<ApiResult<List<AbnormalHandlerDto>>> Handlers() =>
        _svc.HandlersAsync(JwtHelper.GetFactoryId(User));

    [HttpPost("{id:long}/resolve")]
    public Task<ApiResult<object?>> Resolve(long id, [FromBody] AbnormalResolveDto dto)
    {
        var role = JwtHelper.GetRole(User);
        if (role != 1 && role != 3)
            throw ThrowHelper.BizUser("你没有处理异常的权限，只有管理员或班组长才能处理。请把这个问题告诉他们来处理。");
        return _svc.ResolveAsync(id, dto ?? new AbnormalResolveDto(), JwtHelper.GetUserId(User), JwtHelper.GetFactoryId(User));
    }
}
