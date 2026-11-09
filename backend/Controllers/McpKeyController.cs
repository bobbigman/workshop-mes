using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Mcp;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Controllers;

/// <summary>MCP 钥匙管理：每厂一把；复制报文须再输登录密码。仅管理员（RoleGuard）。</summary>
[ApiController]
[TypeFilter(typeof(CurrentAdminFilter))]
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
    public Task<ApiResult<List<McpKeyFactoryOptionDto>>> Factories() =>
        _svc.ListFactoriesAsync(JwtHelper.GetFactoryId(User));

    /// <summary>本厂当前钥匙状态（无列表）。</summary>
    [HttpGet]
    public Task<ApiResult<McpKeyStatusDto>> Status() =>
        _svc.GetStatusAsync(JwtHelper.GetFactoryId(User));

    /// <summary>查看/复制完整连接报文：须再输当前管理员登录密码。</summary>
    [HttpPost("connection")]
    public Task<ApiResult<McpKeyConnectionDto>> Connection([FromBody] McpKeyRevealDto? dto) =>
        _svc.GetConnectionAsync(
            JwtHelper.GetFactoryId(User),
            JwtHelper.GetUserId(User),
            dto?.Password,
            ResolveMcpUrl());

    /// <summary>生成或重新生成（会吊销旧钥）。</summary>
    [HttpPost]
    public Task<ApiResult<McpKeyCreateResultDto>> Create() =>
        _svc.CreateAsync(JwtHelper.GetFactoryId(User), ResolveMcpUrl());

    /// <summary>吊销本厂当前有效钥匙（不断开后不自动发新钥）。</summary>
    [HttpPost("revoke")]
    public Task<ApiResult<object?>> Revoke() =>
        _svc.RevokeActiveAsync(JwtHelper.GetFactoryId(User));

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
