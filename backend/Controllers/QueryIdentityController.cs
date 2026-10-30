using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ahu.MicrosoftMes.Controllers;

[ApiController, Route("api/QueryIdentity"), Authorize]
public sealed class QueryIdentityController(McpQueryIdentityService service) : ControllerBase
{
    [HttpPost("inspect")]
    public async Task<ApiResult<object>> Inspect(ReportIdentityApprovalDto dto, CancellationToken ct)
    {
        var lookup = await service.InspectAsync(dto.LookupToken, JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User), ct);
        return ApiResult<object>.Ok(new { lookup.UserName, lookup.FactoryName, lookup.ExpiresAt });
    }

    [HttpPost("approve")]
    public async Task<ApiResult<object>> Approve(ReportIdentityApprovalDto dto, CancellationToken ct)
        => ApiResult<object>.Ok(new { verificationCode = await service.ApproveAsync(dto.LookupToken,
            JwtHelper.GetFactoryId(User), JwtHelper.GetUserId(User), dto.Password, ct), expireMinutes = 5 });
}
