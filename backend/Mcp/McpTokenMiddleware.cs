using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using Microsoft.EntityFrameworkCore;

namespace ahu.MicrosoftMes.Mcp;

/// <summary>
/// MCP 入口鉴权：按钥匙定厂（docs/96）+ WorkBuddy/配置万能钥匙（docs/99）。
/// 只拦 /mcp；明文钥匙永不打日志。
/// </summary>
public class McpTokenMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _config;
    private readonly ILogger<McpTokenMiddleware> _logger;

    public McpTokenMiddleware(RequestDelegate next, IConfiguration config, ILogger<McpTokenMiddleware> logger)
    {
        _next = next;
        _config = config;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/mcp"))
        {
            await _next(context);
            return;
        }

        var presented = ExtractPresentedKey(context.Request);
        if (string.IsNullOrEmpty(presented))
        {
            _logger.LogWarning("MCP 未带钥匙: {Path} 来自 {Ip}", context.Request.Path, context.Connection.RemoteIpAddress);
            await Write401(context);
            return;
        }

        var mcpCtx = context.RequestServices.GetRequiredService<IMcpRequestContext>();
        var db = context.RequestServices.GetRequiredService<AppDbContext>();

        // 1) 配置万能钥匙 / WorkBuddy 专用钥匙 → 用 Mcp:FactoryId
        var masterToken = _config["Mcp:Token"];
        var workBuddyToken = _config["Mcp:WorkBuddyToken"];
        string? alias = null;
        long? factoryId = null;

        if (!string.IsNullOrWhiteSpace(masterToken)
            && string.Equals(presented, masterToken, StringComparison.Ordinal))
        {
            alias = "config-master";
            factoryId = _config.GetValue<long>("Mcp:FactoryId", 0);
        }
        else if (!string.IsNullOrWhiteSpace(workBuddyToken)
            && string.Equals(presented, workBuddyToken, StringComparison.Ordinal))
        {
            alias = "workbuddy-config";
            factoryId = _config.GetValue<long>("Mcp:FactoryId", 0);
        }
        else
        {
            // 2) 表内厂钥匙：遍历有效行 BCrypt.Verify
            var now = DateTime.Now;
            var keys = await db.McpKeys.AsNoTracking()
                .Where(k => k.Status == 1 && (k.ExpireAt == null || k.ExpireAt > now))
                .Select(k => new { k.KeyId, k.FactoryId, k.KeyAlias, k.ApiKeyHash })
                .ToListAsync();

            foreach (var k in keys)
            {
                if (PasswordHelper.Verify(presented, k.ApiKeyHash))
                {
                    alias = k.KeyAlias;
                    factoryId = k.FactoryId;
                    break;
                }
            }
        }

        if (factoryId is null or <= 0 || string.IsNullOrEmpty(alias))
        {
            _logger.LogWarning("MCP 钥匙无效: {Path} 来自 {Ip}", context.Request.Path, context.Connection.RemoteIpAddress);
            await Write401(context);
            return;
        }

        // 标签对账：若请求带 factoryid / X-Factory-Id，必须与钥匙定的厂一致
        var label = context.Request.Headers["factoryid"].FirstOrDefault()
            ?? context.Request.Headers["X-Factory-Id"].FirstOrDefault()
            ?? context.Request.Query["factoryid"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(label)
            && long.TryParse(label.Trim(), out var labelFid)
            && labelFid != factoryId.Value)
        {
            _logger.LogWarning(
                "MCP 厂标签与钥匙不符: alias={Alias} keyFactory={KeyFactory} label={Label} path={Path}",
                alias, factoryId, labelFid, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"code\":403,\"msg\":\"factory label mismatch\"}");
            return;
        }

        var factoryName = await db.Factories.AsNoTracking()
            .Where(f => f.Id == factoryId.Value)
            .Select(f => f.FactoryName)
            .FirstOrDefaultAsync() ?? $"#{factoryId}";

        mcpCtx.CurrentFactoryId = factoryId;
        mcpCtx.KeyAlias = alias;
        mcpCtx.FactoryName = factoryName;

        _logger.LogInformation(
            "MCP 放行 alias={Alias} factory={FactoryName}({FactoryId}) path={Path} ip={Ip}",
            alias, factoryName, factoryId, context.Request.Path, context.Connection.RemoteIpAddress);

        await _next(context);
    }

    private static string? ExtractPresentedKey(HttpRequest request)
    {
        var auth = request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth["Bearer ".Length..].Trim();

        var queryToken = request.Query["token"].ToString();
        if (string.IsNullOrEmpty(queryToken))
            queryToken = request.Query["access_token"].ToString();
        return string.IsNullOrWhiteSpace(queryToken) ? null : queryToken.Trim();
    }

    private static async Task Write401(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"code\":401,\"msg\":\"unauthorized\"}");
    }
}
