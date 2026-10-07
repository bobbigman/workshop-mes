using System.ComponentModel;
using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;
using ModelContextProtocol.Server;

namespace ahu.MicrosoftMes.Mcp;

/// <summary>
/// MCP 工具：检索服务器操作手册（SupportDocs）。复用 SupportDocumentService，
/// 与 PC 智能客服 search_support_docs 同一目录、同一检索；不暴露磁盘路径。
/// </summary>
[McpServerToolType]
public class SupportDocsMcpTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SupportDocumentService _supportDocs;
    private readonly IMcpWxBindService _wxBind;
    private readonly IConfiguration _config;

    public SupportDocsMcpTools(
        SupportDocumentService supportDocs,
        IMcpAuthService authService,
        IMcpWxBindService wxBind,
        IConfiguration config)
    {
        _supportDocs = supportDocs;
        _wxBind = wxBind;
        _config = config;
    }

    [McpServerTool, Description("search_support_docs：检索服务器上的操作说明/常见问题/数据口径（怎么扫码报工、怎么补报、无报工权限等）。只传 query，不要传任何文件路径。演示模式下无需授权码；正式模式需钥匙定厂及手机号绑定，不需金蝶授权码。返回 state/message/sources 摘录，据此回答，禁止另开微信小程序等方案。")]
    public async Task<string> SearchSupportDocs(
        [Description("用户问题或关键词，必填，最多 500 字")] string query,
        [Description("兼容旧调用保留的参数；车间查询不使用金蝶授权码")] string? authToken = null,
        [Description("渠道，可空")] string? chanType = null,
        [Description("会话身份，可空")] string? sessionId = null)
    {
        var access = await McpDataAccess.ResolveAsync(_wxBind, _config, chanType, sessionId);
        if (access.Hint != null) return access.Hint;

        if (string.IsNullOrWhiteSpace(query))
            throw ThrowHelper.Biz(nameof(SearchSupportDocs), "query 必填");
        var q = query.Trim();
        if (q.Length > 500)
            throw ThrowHelper.Biz(nameof(SearchSupportDocs), "query 最多 500 字符");

        var result = _supportDocs.Search(q);
        return JsonSerializer.Serialize(new
        {
            state = result.State,
            message = result.Message,
            truncated = result.Truncated,
            totalMatched = result.TotalMatched,
            warnings = result.Warnings,
            sources = result.Sources.Select(s => new
            {
                sourceId = s.SourceId,
                title = s.Title,
                chapter = s.Chapter,
                version = s.Version,
                updatedAt = s.UpdatedAt,
                excerpt = s.Excerpt
            })
        }, JsonOpts);
    }
}
