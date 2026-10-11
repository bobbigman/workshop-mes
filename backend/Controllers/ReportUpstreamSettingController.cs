using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

/// <summary>报工上级工序校验开关（docs/150）</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportUpstreamSettingController : ControllerBase
{
    private readonly IReportUpstreamSettingService _svc;
    public ReportUpstreamSettingController(IReportUpstreamSettingService svc) { _svc = svc; }

    [HttpGet]
    public Task<ApiResult<ReportUpstreamSettingDto>> Get() =>
        _svc.GetAsync(JwtHelper.GetFactoryId(User));

    [HttpPut]
    public Task<ApiResult<object?>> Save([FromBody] ReportUpstreamSettingDto dto) =>
        _svc.SaveAsync(dto, JwtHelper.GetFactoryId(User), JwtHelper.GetRole(User));
}
