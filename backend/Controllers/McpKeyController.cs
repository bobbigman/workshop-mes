using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Mcp;

namespace ahu.MicrosoftMes.Controllers;

/// <summary>MCP 钥匙管理（docs/96、docs/147）：一键生成完整连接报文 / 列表 / 吊销。仅管理员（RoleGuard）。</summary>
[ApiController]
[Route("api/McpKey")]
[Authorize]
public class McpKeyController : ControllerBase
{
    private readonly IMcpKeyService _svc;
    private readonly IConfiguration _config;

    public McpKeyController(IMcpKeyService svc, IConfiguration config)
    {
        _svc = svc;
        _config = config;
    }

    [HttpGet("factories")]
    public Task<ApiResult<List<McpKeyFactoryOptionDto>>> Factories() => _svc.ListFactoriesAsync();

    [HttpGet]
    public Task<ApiResult<List<McpKeyListItemDto>>> List() =>
        _svc.ListAsync(JwtHelper.GetFactoryId(User));

    [HttpPost]
    public Task<ApiResult<McpKeyCreateResultDto>> Create() =>
        _svc.CreateAsync(JwtHelper.GetFactoryId(User), ResolveMcpUrl());

    [HttpPost("{id:long}/revoke")]
    public Task<ApiResult<object?>> Revoke(long id) =>
        _svc.RevokeAsync(id, JwtHelper.GetFactoryId(User));

    /// <summary>优先 Mcp:Url；否则当前请求站点 + /mcp。不得 hardcode 演示域名。</summary>
    private string ResolveMcpUrl()
    {
        var configured = (_config["Mcp:Url"] ?? "").Trim();
        if (!string.IsNullOrEmpty(configured))
            return configured.TrimEnd('/');

        var baseUrl = $"{Request.Scheme}://{Request.Host}".TrimEnd('/');
        return $"{baseUrl}/mcp";
    }
}
