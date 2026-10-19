using System.ComponentModel;
using ModelContextProtocol.Server;

namespace ahu.MicrosoftMes.Mcp;

/// <summary>MCP 工具：已核验本人入口或普通H5首页（docs/249）。</summary>
[McpServerToolType]
public class ReportLinkMcpTools
{
    private readonly IMcpReportLinkService _svc;

    public ReportLinkMcpTools(IMcpReportLinkService svc) => _svc = svc;

    [McpServerTool(Name = "get_report_link_by_identity"),
     Description("获取H5报工入口，不提交报工。默认帮我报工/语音报工使用本入口；已有本人个人码或免密链接可直接使用，不强制重新验证。新获取专属链接须可信平台用户身份、管理员核验映射与有效员工绑定一致，identity仅核对本人，姓名/手机号/一句确认不能授予登录权限。未关联时返回use_existing_login_or_personal_code及不含凭证的publicEntryUrl，原样发送普通首页并说明已有H5登录直接进入，或扫描管理员提供的本人个人登录码；不把普通首页称为以某姓名免密登录，不索要密码或8位码，不再反复按姓名重试。成功返回完整link和cardText，原样发送，并说明也可用本人个人登录码。仅原MCP工厂范围，不生成二维码图片、不获取其他员工链接。仅用户明确要在聊天直接完成时才走独立聊天授权工具。")]
    public Task<string> GetReportLinkByIdentity(
        [Description("可空；仅核对当前已验证本人的准确姓名、账号或完整手机号，不能据此选择其他员工")] string? identity = null)
        => _svc.GetReportLinkByIdentityAsync(identity);
}
