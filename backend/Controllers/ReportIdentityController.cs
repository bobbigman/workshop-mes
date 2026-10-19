using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ahu.MicrosoftMes.Controllers;

[ApiController, Route("api/ReportIdentity"), Authorize]
public class ReportIdentityController(IReportIdentityService service) : ControllerBase
{
    [HttpPost("inspect")]
    public async Task<ApiResult<object>> Inspect([FromBody] ReportIdentityApprovalDto dto)
    {
        var lookup = await service.InspectAsync(dto.LookupToken, JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User));
        return ApiResult<object>.Ok(new { lookup.UserName, lookup.FactoryName, lookup.ExpiresAt });
    }
    [HttpPost("approve")]
    public async Task<ApiResult<object>> Approve([FromBody] ReportIdentityApprovalDto dto)
        => ApiResult<object>.Ok(new { verificationCode = await service.ApproveAsync(dto.LookupToken,
            JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User), dto.Password), expireMinutes = 5 });
}

public class ReportIdentityApprovalDto
{
    public string LookupToken { get; set; } = "";
    public string Password { get; set; } = "";
}
