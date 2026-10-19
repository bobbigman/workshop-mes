using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

/// <summary>
/// 企微后台专用报工入口（docs/241）：应用凭据鉴权 + userid 认人；不用工人 JWT。
/// 现有 POST /api/Report 原样不动。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class WecomReportController : ControllerBase
{
    private readonly IWecomReportService _svc;
    private readonly IConfiguration _config;

    public WecomReportController(IWecomReportService svc, IConfiguration config)
    {
        _svc = svc;
        _config = config;
    }

    /// <summary>预览：校验 + 回显确认卡 + 短时一次性 token，不落库。</summary>
    [HttpPost("preview")]
    public Task<ApiResult<WecomReportPreviewResultDto>> Preview([FromBody] WecomReportPreviewDto dto)
    {
        var factoryId = EnsureWecomAuth();
        return _svc.PreviewAsync(dto, factoryId);
    }

    /// <summary>确认：带 token 落库；同一 token 幂等只落一次。</summary>
    [HttpPost("confirm")]
    public Task<ApiResult<object?>> Confirm([FromBody] WecomReportConfirmDto dto)
    {
        var factoryId = EnsureWecomAuth();
        return _svc.ConfirmAsync(dto, factoryId);
    }

    /// <summary>
    /// 企微应用身份：请求头 X-Wecom-Token（或 Authorization: Bearer）= WecomReport:ApiToken；
    /// 厂由 WecomReport:FactoryId 定。Token 空则全部拒绝。
    /// </summary>
    private long EnsureWecomAuth()
    {
        var expected = (_config["WecomReport:ApiToken"] ?? "").Trim();
        if (string.IsNullOrEmpty(expected))
            throw ThrowHelper.Biz(nameof(EnsureWecomAuth), "未配置 WecomReport:ApiToken，拒绝企微报工调用");

        var got = (Request.Headers["X-Wecom-Token"].FirstOrDefault() ?? "").Trim();
        if (string.IsNullOrEmpty(got))
        {
            var auth = Request.Headers.Authorization.ToString();
            if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                got = auth["Bearer ".Length..].Trim();
        }

        if (!string.Equals(got, expected, StringComparison.Ordinal))
            throw ThrowHelper.BizUser("未授权的企微报工调用");

        var factoryId = _config.GetValue<long>("WecomReport:FactoryId", 0);
        if (factoryId <= 0)
            throw ThrowHelper.Biz(nameof(EnsureWecomAuth), "未配置 WecomReport:FactoryId");

        return factoryId;
    }
}
