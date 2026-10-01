using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Mcp;

namespace ahu.MicrosoftMes.Controllers;

/// <summary>MCP 钥匙管理（docs/96）：生成 / 列表 / 吊销。仅管理员（RoleGuard）。</summary>
[ApiController]
[Route("api/McpKey")]
[Authorize]
public class McpKeyController : ControllerBase
{
    private readonly IMcpKeyService _svc;

    public McpKeyController(IMcpKeyService svc) => _svc = svc;

    [HttpGet("factories")]
    public Task<ApiResult<List<McpKeyFactoryOptionDto>>> Factories() => _svc.ListFactoriesAsync();

    [HttpGet]
    public Task<ApiResult<List<McpKeyListItemDto>>> List([FromQuery] long? factoryId) =>
        _svc.ListAsync(factoryId);

    [HttpPost]
    public Task<ApiResult<McpKeyCreateResultDto>> Create([FromBody] McpKeyCreateDto dto) =>
        _svc.CreateAsync(dto);

    [HttpPost("{id:long}/revoke")]
    public Task<ApiResult<object?>> Revoke(long id) => _svc.RevokeAsync(id);
}
