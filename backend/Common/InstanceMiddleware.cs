using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Common;

/// <summary>
/// 已登录请求校验 JWT 中的工厂 Id 属于本实例配置的工厂，防止跨实例误用旧 token（密钥已不同时仍作双保险）。
/// </summary>
public class InstanceFactoryGuardMiddleware
{
    private readonly RequestDelegate _next;

    public InstanceFactoryGuardMiddleware(RequestDelegate next) { _next = next; }

    public async Task InvokeAsync(HttpContext context, InstanceRuntime runtime, AppDbContext db)
    {
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var fid = JwtHelper.GetFactoryId(context.User);
            // docs/52：多账套——JWT 工厂不必等于启动配置工厂；只要在 sys_factory 存在即放行。
            // 跨实例误用旧 token 已由 JWT 签名（每实例密钥不同）兜底，此处仅拦截「不存在的工厂」。
            var isConfigured = runtime.FactoryId.HasValue && fid == runtime.FactoryId.Value;
            var exists = isConfigured || await db.Factories.AnyAsync(f => f.Id == fid);
            if (!exists)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { code = 401, msg = "登录凭证不属于本实例，请重新登录" });
                return;
            }
        }

        await _next(context);
    }
}

/// <summary>MCP / 配对授权入口总闸：未启用时直接 404；已启用仍须旗舰档（KingdeeMcp）。</summary>
public class McpGateMiddleware
{
    private readonly RequestDelegate _next;
    private readonly InstanceOptions _opt;

    public McpGateMiddleware(RequestDelegate next, IOptions<InstanceOptions> opt)
    {
        _next = next;
        _opt = opt.Value;
    }

    public async Task InvokeAsync(HttpContext context, IConfiguration config, ILicenseTierService license)
    {
        var path = context.Request.Path.Value ?? "";
        var isMcp = path.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/mcp-auth", StringComparison.OrdinalIgnoreCase);

        if (isMcp)
        {
            if (!_opt.EnableMcp)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(new { code = 404, msg = "MCP 未启用" });
                return;
            }

            var factoryId = config.GetValue<long>("Mcp:FactoryId", 1);
            await license.EnsureFeatureAsync(factoryId, LicenseFeature.KingdeeMcp);
        }

        await _next(context);
    }
}
