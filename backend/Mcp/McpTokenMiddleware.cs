using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Services;
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

        var labels = context.Request.Headers["factoryid"].Concat(context.Request.Headers["X-Factory-Id"])
            .Concat(context.Request.Query["factoryid"]).ToArray();
        if (labels.Any(label => !long.TryParse(label, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var id) || id <= 0))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { code = 400, msg = "工厂标签格式无效" });
            return;
        }
        if (labels.Any(label => long.Parse(label!, System.Globalization.CultureInfo.InvariantCulture) != factoryId.Value))
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { code = 403, msg = "工厂标签与访问权限不符" });
            return;
        }
        var factory = await db.Factories.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == factoryId.Value && f.TrialCleanedAtUtc == null);
        if (factory == null)
        {
            await Write401(context);
            return;
        }
        var factoryName = factory.FactoryName;
        await context.RequestServices.GetRequiredService<ILicenseTierService>()
            .EnsureFeatureAsync(factoryId.Value, LicenseFeature.KingdeeMcp);

        mcpCtx.CurrentFactoryId = factoryId;
        mcpCtx.KeyAlias = alias;
        mcpCtx.FactoryName = factoryName;

        _logger.LogInformation(
            "MCP 放行 alias={Alias} factory={FactoryName}({FactoryId}) path={Path} ip={Ip}",
            alias, factoryName, factoryId, context.Request.Path, context.Connection.RemoteIpAddress);

        // docs/149 临时探活已关（每请求 Warning 写盘）；若再查 X-Mcp-Session-Id，取消下方注释即可。
        // LogSessionProbe(context.Request, alias);

        await _next(context);
    }

    // /// <summary>docs/149 临时探活：有无 X-Mcp-Session-Id + SHA256 前 8 + 渠道；不打明文 session。</summary>
    // private void LogSessionProbe(HttpRequest request, string keyAlias)
    // {
    //     var raw = request.Headers["X-Mcp-Session-Id"].FirstOrDefault();
    //     var present = !string.IsNullOrWhiteSpace(raw);
    //     var prefix = present
    //         ? McpWxBindService.HashSession(raw!.Trim())[..8]
    //         : "-";
    //     var chan = request.Headers["X-Mcp-Channel"].FirstOrDefault()
    //         ?? request.Headers["X-Mcp-Chan-Type"].FirstOrDefault()
    //         ?? keyAlias;
    //     // Warning：文件 sink 最低级是 Warning，Information 只进控制台、盘上看不到
    //     _logger.LogWarning(
    //         "MCP会话探活149 sessionPresent={Present} hashPrefix={Prefix} chan={Chan} path={Path}",
    //         present, prefix, chan, request.Path);
    // }

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
