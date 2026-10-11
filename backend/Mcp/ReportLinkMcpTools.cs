using System.ComponentModel;
using ModelContextProtocol.Server;

namespace ahu.MicrosoftMes.Mcp;

/// <summary>MCP 工具：工人自报凭证拿专属免密报工链接（docs/215）。</summary>
[McpServerToolType]
public class ReportLinkMcpTools
{
    private readonly IMcpReportLinkService _svc;

    public ReportLinkMcpTools(IMcpReportLinkService svc) => _svc = svc;

    [McpServerTool(Name = "get_report_link_by_identity"),
     Description("工人要报工时：用手机号(11位)、登录账号或姓名换取专属免密报工链接。命中一人返回 link（完整 https 绝对地址）与 cardText。必须把 link 原样发给用户做可点链接，禁止改成 localhost、禁止改域名/协议/路径。查无/停用/重名不返回链接。不需会话绑定、不需金蝶授权码。查工单进度请用其它工具，不要用本工具。")]
    public Task<string> GetReportLinkByIdentity(
        [Description("工人自报凭证：11位手机号，或本厂登录账号，或姓名（重名时请改用手机号/账号）")] string identity)
        => _svc.GetReportLinkByIdentityAsync(identity);
}
