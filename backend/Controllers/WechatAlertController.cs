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
    private readonly WechatEventService _events;

    public WechatAlertController(IWechatAlertService svc, IConfiguration config, WechatEventService events)
    {
        _svc = svc;
        _config = config;
        _events = events;
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
        ValidateCronToken();

        return _svc.PushDueAlertCronAsync();
    }

    [HttpGet("rules")]
    [Authorize(Roles = "1")]
    [RequireLicenseFeature(LicenseFeature.WechatDueAlert)]
    public async Task<ApiResult<object?>> Rules() => ApiResult<object?>.Ok(await _events.RulesAsync(JwtHelper.GetFactoryId(User)));

    [HttpPost("rules")]
    [Authorize(Roles = "1")]
    [RequireLicenseFeature(LicenseFeature.WechatDueAlert)]
    public async Task<ApiResult<object?>> CreateRule([FromBody] WechatRuleDto dto)
    {
        await _events.SaveRuleAsync(null, dto, JwtHelper.GetFactoryId(User));
        return ApiResult<object?>.OkMsg();
    }

    [HttpPut("rules/{id:long}")]
    [Authorize(Roles = "1")]
    [RequireLicenseFeature(LicenseFeature.WechatDueAlert)]
    public async Task<ApiResult<object?>> UpdateRule(long id, [FromBody] WechatRuleDto dto)
    {
        await _events.SaveRuleAsync(id, dto, JwtHelper.GetFactoryId(User));
        return ApiResult<object?>.OkMsg();
    }

    [HttpGet("deliveries")]
    [Authorize(Roles = "1")]
    [RequireLicenseFeature(LicenseFeature.WechatDueAlert)]
    public async Task<ApiResult<object?>> Deliveries(int page = 1, int pageSize = 20) =>
        ApiResult<object?>.Ok(await _events.DeliveriesAsync(JwtHelper.GetFactoryId(User), page, pageSize));

    [HttpGet("deliveries/{id:long}/attempts")]
    [Authorize(Roles = "1")]
    [RequireLicenseFeature(LicenseFeature.WechatDueAlert)]
    public async Task<ApiResult<object?>> Attempts(long id) => ApiResult<object?>.Ok(await _events.AttemptsAsync(JwtHelper.GetFactoryId(User), id));

    [HttpPost("deliveries/{id:long}/retry")]
    [Authorize(Roles = "1")]
    [RequireLicenseFeature(LicenseFeature.WechatDueAlert)]
    public async Task<ApiResult<object?>> Retry(long id)
    {
        await _events.HandleAsync(JwtHelper.GetFactoryId(User), id, JwtHelper.GetUserId(User), new(), false);
        return new ApiResult<object?> { Msg = "已加入待发队列" };
    }

    [HttpPost("deliveries/{id:long}/verify")]
    [Authorize(Roles = "1")]
    [RequireLicenseFeature(LicenseFeature.WechatDueAlert)]
    public async Task<ApiResult<object?>> Verify(long id, [FromBody] WechatDeliveryActionDto dto)
    {
        await _events.HandleAsync(JwtHelper.GetFactoryId(User), id, JwtHelper.GetUserId(User), dto, true);
        return ApiResult<object?>.OkMsg();
    }

    [HttpPost("dispatch-events")]
    [AllowAnonymous]
    public Task<ApiResult<object?>> DispatchEvents()
    {
        ValidateCronToken();
        return _events.DispatchAsync();
    }

    private void ValidateCronToken()
    {
        var expected = (_config["WechatAlert:CronToken"] ?? "").Trim();
        if (string.IsNullOrEmpty(expected))
            throw ThrowHelper.Biz(nameof(CronPush), "未配置 WechatAlert:CronToken，拒绝定时推送");

        var got = (Request.Headers["X-Cron-Token"].FirstOrDefault() ?? "").Trim();
        if (!string.Equals(got, expected, StringComparison.Ordinal))
            throw ThrowHelper.Biz(nameof(CronPush), "定时推送 Token 无效");

    }
}
