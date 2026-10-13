using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ahu.MicrosoftMes.Common;

namespace ahu.MicrosoftMes.Controllers;

/// <summary>匿名实例展示信息（登录页用）；禁止返回连接串/密钥。</summary>
[ApiController]
[Route("api/instance-info")]
[AllowAnonymous]
public class InstanceController : ControllerBase
{
    private readonly InstanceOptions _opt;
    private readonly SupportOptions _support;

    public InstanceController(IOptions<InstanceOptions> opt, IOptions<SupportOptions> support)
    {
        _opt = opt.Value;
        _support = support.Value;
    }

    [HttpGet]
    public ApiResult<object> Get()
    {
        return ApiResult<object>.Ok(new
        {
            code = _opt.Code,
            displayName = _opt.DisplayName,
            // 仅提示前端本实例工厂；不作为选厂入口
            factoryCode = _opt.FactoryCode,
            version = AppVersion.Current,
            // docs/111：失败兜底「请洽…」称呼；空则前端用默认文案
            supportProviderName = (_support.ProviderName ?? "").Trim(),
            // docs/213：空=胡工单方版；有值=联合出品伙伴名（胡工锁代码常量）
            creditPartner = (_opt.CreditPartner ?? "").Trim()
        });
    }
}
