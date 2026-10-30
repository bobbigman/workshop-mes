using System.ComponentModel;
using ahu.MicrosoftMes.Mcp;
using ModelContextProtocol.Server;

namespace ahu.MicrosoftMes.Mcp;

/// <summary>MCP 工具：手机号直绑 / 解绑（docs/97）。</summary>
[McpServerToolType]
public class WxBindMcpTools
{
    private readonly IMcpWxBindService _bind;

    public WxBindMcpTools(IMcpWxBindService bind) => _bind = bind;

    [McpServerTool, Description("旧绑定入口，仅返回本人网页验证指引；请改用start_query_auth，手机号不单独授权。")]
    public Task<string> BindWechat(
        [Description("本厂用户手机号，11 位")] string phone,
        [Description("渠道：coze/doubao/workbuddy/wx_gzh/wecom，可空默认 coze")] string? chanType = null,
        [Description("会话稳定身份 ID；可空则读请求头 X-Mcp-Session-Id")] string? sessionId = null)
        => _bind.BindAsync(phone, chanType, sessionId);

    [McpServerTool, Description("撤销本人的查询授权及绑定，必须提供本人queryAccessToken或可信平台身份。")]
    public Task<string> UnbindWechat(
        [Description("本人只读查询授权凭证，可空时必须有可信平台身份")] string? queryAccessToken = null,
        [Description("渠道，可空默认 coze")] string? chanType = null,
        [Description("会话稳定身份 ID；可空则读请求头 X-Mcp-Session-Id")] string? sessionId = null)
        => _bind.UnbindQueryAsync(queryAccessToken);
}
