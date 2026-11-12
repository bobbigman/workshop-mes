using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DocumentPermissionController : ControllerBase
{
    private readonly IDocumentPermissionService _svc;
    public DocumentPermissionController(IDocumentPermissionService svc) { _svc = svc; }

    /// <summary>读本厂单据权限；登录用户可读（前端藏编辑按钮）。</summary>
    [HttpGet]
    public Task<ApiResult<DocumentPermissionDto>> Get() =>
        _svc.GetAsync(JwtHelper.GetFactoryId(User));

    [HttpPut]
    public Task<ApiResult<object?>> Save([FromBody] DocumentPermissionDto dto) =>
        _svc.SaveAsync(dto, JwtHelper.GetFactoryId(User), JwtHelper.GetRole(User));
}
