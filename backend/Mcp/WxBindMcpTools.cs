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

    [McpServerTool, Description("用手机号绑定当前会话到本厂账号。报 11 位手机号即可；无需验证码。绑定成功后同会话查数据免再报手机号。")]
    public Task<string> BindWechat(
        [Description("本厂用户手机号，11 位")] string phone,
        [Description("渠道：coze/doubao/workbuddy/wx_gzh/wecom，可空默认 coze")] string? chanType = null,
        [Description("会话稳定身份 ID；可空则读请求头 X-Mcp-Session-Id")] string? sessionId = null)
        => _bind.BindAsync(phone, chanType, sessionId);

    [McpServerTool, Description("解绑当前会话与本厂账号的绑定。解绑后需重新报手机号绑定。")]
    public Task<string> UnbindWechat(
        [Description("渠道，可空默认 coze")] string? chanType = null,
        [Description("会话稳定身份 ID；可空则读请求头 X-Mcp-Session-Id")] string? sessionId = null)
        => _bind.UnbindAsync(chanType, sessionId);
}
