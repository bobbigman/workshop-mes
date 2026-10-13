using System.ComponentModel;
using System.Text.Json;
using ahu.MicrosoftMes.Services;
using ModelContextProtocol.Server;

namespace ahu.MicrosoftMes.Mcp;

[McpServerToolType]
public class AuthMcpTools
{
    private readonly IMcpAuthService _authService;
    private readonly IMcpRequestContext _mcpCtx;
    private readonly IConfiguration _config;

    public AuthMcpTools(IMcpAuthService authService, IMcpRequestContext mcpCtx, IConfiguration config)
    {
        _authService = authService;
        _mcpCtx = mcpCtx;
        _config = config;
    }

    [McpServerTool, Description("发起金蝶账号授权。返回一个 H5 授权链接，请用户打开链接登录金蝶，完成后页面会显示授权码，把授权码告诉用户后即可用于查询工单、报工、工资。")]
    public async Task<string> StartAuth()
    {
        long factoryId = _mcpCtx.RequireFactoryId(nameof(StartAuth));
        var code = await _authService.CreateCodeAsync(factoryId);

        var baseUrl = (_config["Mcp:AuthBaseUrl"] ?? "").TrimEnd('/');
        var localFallback = string.IsNullOrEmpty(baseUrl);
        if (localFallback)
            baseUrl = (_config["Mcp:AuthLocalBaseUrl"] ?? "http://localhost:8080").TrimEnd('/');

        var link = $"{baseUrl}/mcp-auth?code={code}";
        return JsonSerializer.Serialize(new
        {
            code = 0,
            link,
            msg = localFallback
                ? "本地自测链接（未配置 Mcp:AuthBaseUrl）。接入方舟前请把 cpolar 公网地址配到 Mcp:AuthBaseUrl。"
                : "请用户打开链接完成金蝶授权，完成后页面会显示一个授权码，让用户把授权码告诉我"
        });
    }
}
