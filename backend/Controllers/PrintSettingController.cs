using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireLicenseFeature(LicenseFeature.PrintLabel)]
public class PrintSettingController : ControllerBase
{
    private readonly IPrintSettingService _svc;
    public PrintSettingController(IPrintSettingService svc) { _svc = svc; }

    [HttpGet]
    public Task<ApiResult<PrintSettingDto>> Get() =>
        _svc.GetAsync(JwtHelper.GetFactoryId(User));

    [HttpPut]
    public Task<ApiResult<object?>> Save([FromBody] PrintSettingDto dto) =>
        _svc.SaveAsync(dto, JwtHelper.GetFactoryId(User));
}
