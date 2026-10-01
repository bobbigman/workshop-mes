using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

/// <summary>运维/排查用只读 SQL。仅管理员；不挂 MCP（docs/59）。</summary>
[ApiController]
[Authorize]
[Route("api/diag")]
public class DiagController : ControllerBase
{
    private readonly IDiagSqlService _svc;

    public DiagController(IDiagSqlService svc) { _svc = svc; }

    private void EnsureAdmin()
    {
        if (JwtHelper.GetRole(User) != 1)
            throw ThrowHelper.BizUser("仅管理员可使用诊断查询");
    }

    [HttpPost("sql")]
    public Task<ApiResult<DiagSqlResultDto>> Sql([FromBody] DiagSqlRequest req, CancellationToken ct)
    {
        EnsureAdmin();
        return _svc.QueryAsync(req?.Sql ?? "", JwtHelper.GetUserId(User), ct);
    }
}
