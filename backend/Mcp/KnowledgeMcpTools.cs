using System.ComponentModel;
using System.Text.Json;
using ahu.MicrosoftMes.Services;
using ModelContextProtocol.Server;

namespace ahu.MicrosoftMes.Mcp;

[McpServerToolType]
public class KnowledgeMcpTools
{
    private readonly IKnowledgeService _knowledgeService;
    private readonly IMcpRequestContext _mcpCtx;
    private readonly IMcpWxBindService _wxBind;
    private readonly IConfiguration _config;

    public KnowledgeMcpTools(
        IKnowledgeService knowledgeService,
        IMcpAuthService authService,
        IMcpRequestContext mcpCtx,
        IMcpWxBindService wxBind,
        IConfiguration config)
    {
        _knowledgeService = knowledgeService;
        _mcpCtx = mcpCtx;
        _wxBind = wxBind;
        _config = config;
    }

    [McpServerTool, Description("查询某产品如何生产（制造知识库）。按产品编号或名称，自动顺工艺路线，把挂在产品和各工序上的工艺文件读成文字返回。演示模式下无需授权码；正式模式需钥匙定厂及手机号绑定，不需金蝶授权码。")]
    public async Task<string> QueryProductionGuide(
        [Description("兼容旧调用保留的参数；车间查询不使用金蝶授权码")] string? authToken = null,
        [Description("产品编号（精确匹配，推荐）")] string? productCode = null,
        [Description("产品名称（模糊匹配）")] string? productName = null,
        [Description("工序名称（可选，定位到某道工序；不传=整条路线）")] string? operationName = null,
        [Description("渠道，可空")] string? chanType = null,
        [Description("会话身份，可空")] string? sessionId = null)
    {
        var access = await McpDataAccess.ResolveAsync(_wxBind, _config, chanType, sessionId);
        if (access.Hint != null) return access.Hint;

        long factoryId = _mcpCtx.RequireFactoryId(nameof(QueryProductionGuide));
        var result = await _knowledgeService.QueryProductionGuideAsync(productCode, productName, operationName, factoryId);

        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
