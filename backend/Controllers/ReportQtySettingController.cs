using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

/// <summary>报工数量口径：超报比例、上道不良计不计入（docs/230）</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportQtySettingController : ControllerBase
{
    private readonly IReportQtySettingService _svc;
    public ReportQtySettingController(IReportQtySettingService svc) { _svc = svc; }

    [HttpGet]
    public Task<ApiResult<ReportQtySettingDto>> Get() =>
        _svc.GetAsync(JwtHelper.GetFactoryId(User));

    [HttpPut]
    public Task<ApiResult<object?>> Save([FromBody] ReportQtySettingDto dto) =>
        _svc.SaveAsync(dto, JwtHelper.GetFactoryId(User), JwtHelper.GetRole(User));
}
