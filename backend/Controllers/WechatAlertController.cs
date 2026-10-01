using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WechatAlertController : ControllerBase
{
    private readonly IWechatAlertService _svc;
    private readonly IConfiguration _config;

    public WechatAlertController(IWechatAlertService svc, IConfiguration config)
    {
        _svc = svc;
        _config = config;
    }

    [HttpGet("setting")]
    [RequireLicenseFeature(LicenseFeature.WechatDueAlert)]
    public Task<ApiResult<WechatAlertSettingDto>> GetSetting() =>
        _svc.GetSettingAsync(JwtHelper.GetFactoryId(User));

    [HttpPut("setting")]
    [RequireLicenseFeature(LicenseFeature.WechatDueAlert)]
    public Task<ApiResult<object?>> SaveSetting([FromBody] WechatAlertSettingDto dto) =>
        _svc.SaveSettingAsync(dto, JwtHelper.GetFactoryId(User));

    [HttpPost("notice-seen")]
    [RequireLicenseFeature(LicenseFeature.WechatDueAlert)]
    public Task<ApiResult<object?>> NoticeSeen() =>
        _svc.MarkNoticeSeenAsync(JwtHelper.GetFactoryId(User));

    [HttpPost("test")]
    [RequireLicenseFeature(LicenseFeature.WechatDueAlert)]
    public Task<ApiResult<object?>> Test() =>
        _svc.TestSendAsync(JwtHelper.GetFactoryId(User));

    /// <summary>手动：本厂临期+超期一天一批（JWT）。</summary>
    [HttpPost("push-overdue")]
    [RequireLicenseFeature(LicenseFeature.WechatDueAlert)]
    public Task<ApiResult<object?>> PushOverdue() =>
        _svc.PushOverdueAsync(JwtHelper.GetFactoryId(User));

    /// <summary>Windows 计划任务入口：X-Cron-Token = WechatAlert:CronToken（docs/75）。</summary>
    [HttpPost("cron-push")]
    [AllowAnonymous]
    public Task<ApiResult<object?>> CronPush()
    {
        var expected = (_config["WechatAlert:CronToken"] ?? "").Trim();
        if (string.IsNullOrEmpty(expected))
            throw ThrowHelper.Biz(nameof(CronPush), "未配置 WechatAlert:CronToken，拒绝定时推送");

        var got = (Request.Headers["X-Cron-Token"].FirstOrDefault() ?? "").Trim();
        if (!string.Equals(got, expected, StringComparison.Ordinal))
            throw ThrowHelper.Biz(nameof(CronPush), "定时推送 Token 无效");

        return _svc.PushDueAlertCronAsync();
    }
}
