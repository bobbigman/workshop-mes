using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

/// <summary>工厂授权状态（docs/89）；不含业务数据。</summary>
[ApiController]
[Route("api/license")]
[Authorize]
public class LicenseController : ControllerBase
{
    private readonly ILicenseCloudService _license;

    public LicenseController(ILicenseCloudService license)
    {
        _license = license;
    }

    [HttpGet("status")]
    public async Task<ApiResult<LicenseStatusDto>> Status()
    {
        var factoryId = JwtHelper.GetFactoryId(User);
        var dto = await _license.GetStatusAsync(factoryId);
        return ApiResult<LicenseStatusDto>.Ok(dto);
    }
}
