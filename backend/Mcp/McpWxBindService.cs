using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Mcp;

/// <summary>MCP查询本人验证与旧绑定读取；手机号不单独授权。</summary>
public interface IMcpWxBindService
{
    void LogQueryAccess(string tool, long? userId, bool allowed) { }
    Task<SysUser?> ResolveQueryUserAsync(string? queryAccessToken, string? channel, string? session, CancellationToken ct = default)
        => Task.FromResult<SysUser?>(null);
    Task<string> UnbindQueryAsync(string? queryAccessToken, CancellationToken ct = default)
        => Task.FromResult("""{"code":1,"msg":"请完成本人查询验证后解绑。"}""");
    Task<string> BindAsync(string phone, string? chanType, string? sessionId, CancellationToken ct = default);
    Task<string> UnbindAsync(string? chanType, string? sessionId, CancellationToken ct = default);
    /// <summary>未绑定返回 null；已绑定返回用户。缺会话身份抛错。</summary>
    Task<SysUser?> ResolveBoundUserAsync(string? chanType, string? sessionId, CancellationToken ct = default);
    /// <summary>查询必须通过可信平台身份，演示配置不跳过权限。</summary>
    Task<string?> RequireBoundOrHintAsync(string? chanType, string? sessionId, string toolName, CancellationToken ct = default);
}

public class McpWxBindService : IMcpWxBindService
{
    private readonly AppDbContext _db;
    private readonly IMcpRequestContext _ctx;
    private readonly IConfiguration _config;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<McpWxBindService> _logger;

    public McpWxBindService(
        AppDbContext db,
        IMcpRequestContext ctx,
        IConfiguration config,
        IHttpContextAccessor http,
        ILogger<McpWxBindService> logger)
    {
        _db = db;
        _ctx = ctx;
        _config = config;
        _http = http;
        _logger = logger;
    }

    public void LogQueryAccess(string tool, long? userId, bool allowed)
        => _logger.LogInformation("MCP查询授权 tool={Tool} user={UserId} factory={FactoryId} allowed={Allowed}",
            tool, userId, _ctx.CurrentFactoryId, allowed);

    public Task<string> BindAsync(string phone, string? chanType, string? sessionId, CancellationToken ct = default)
        => Task.FromResult("""{"code":1,"msg":"手机号仅用于定位，不再直接绑定授权。请调用start_query_auth并完成本人网页验证。"}""");

    public Task<string> UnbindAsync(string? chanType, string? sessionId, CancellationToken ct = default)
        => UnbindQueryAsync(null, ct);

    public async Task<string> UnbindQueryAsync(string? queryAccessToken, CancellationToken ct = default)
    {
        var user = await ResolveQueryUserAsync(queryAccessToken, null, null, ct);
        if (user == null) return """{"code":1,"msg":"请先完成本人查询验证，不能按会话参数替他人解绑。"}""";
        var identity = _http.HttpContext!.RequestServices.GetRequiredService<McpQueryIdentityService>();
        var rows = await _db.WxBinds.Where(b => b.FactoryId == user.FactoryId && b.UserId == user.Id && b.Status == 1).ToListAsync(ct);
        foreach (var row in rows) { row.Status = 0; row.UnboundAt = DateTime.Now; }
        await _db.SaveChangesAsync(ct);
        identity.Revoke(user.FactoryId, user.Id);
        return """{"code":0,"msg":"已撤销本人查询授权及绑定，后续需重新验证。"}""";
    }

    public async Task<SysUser?> ResolveQueryUserAsync(string? queryAccessToken, string? channel, string? sessionId, CancellationToken ct = default)
    {
        var factory = _ctx.RequireFactoryId(nameof(ResolveQueryUserAsync));
        if (!string.IsNullOrWhiteSpace(queryAccessToken))
            return await _http.HttpContext!.RequestServices.GetRequiredService<McpQueryIdentityService>()
                .ResolveAsync(factory, queryAccessToken, ct);
        var request = _http.HttpContext?.Request;
        if (!McpQueryIdentityService.HasTrustedIdentity(request, _config)) return null;
        var session = request!.Headers["X-Mcp-Session-Id"].ToString().Trim();
        if (string.IsNullOrEmpty(session) || session.Length > 256) return null;
        var verified = _config.GetValue<long>($"Mcp:WecomReport:VerifiedUsers:{factory}:{HashSession(session)}", 0);
        if (verified <= 0) return null;
        var user = await ResolveBoundUserAsync("wecom", session, ct);
        if (user == null || user.Id != verified) return null;
        return await CurrentAccountAccess.RequireAsync(_db, user.Id, factory, false, ct);
    }

    public async Task<SysUser?> ResolveBoundUserAsync(string? chanType, string? sessionId, CancellationToken ct = default)
    {
        var factoryId = _ctx.RequireFactoryId(nameof(ResolveBoundUserAsync));
        var rawSession = ResolveSessionId(sessionId);
        if (string.IsNullOrEmpty(rawSession))
            throw ThrowHelper.BizUser("缺少会话身份，请调用start_query_auth完成本人网页验证");

        var chan = NormalizeChan(chanType);
        var openHash = HashSession(rawSession);
        var bind = await _db.WxBinds.AsNoTracking()
            .FirstOrDefaultAsync(b =>
                b.FactoryId == factoryId && b.ChanType == chan && b.WxOpenid == openHash && b.Status == 1, ct);
        if (bind == null) return null;

        return await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == bind.UserId && u.FactoryId == factoryId && u.Status == 1, ct);
    }

    public async Task<string?> RequireBoundOrHintAsync(
        string? chanType, string? sessionId, string toolName, CancellationToken ct = default)
    {
        var user = await ResolveQueryUserAsync(null, chanType, sessionId, ct);
        if (user == null)
            return """{"code":1,"msg":"请调用start_query_auth完成本人网页验证"}""";

        if (user.Role is not (1 or 2 or 3) || (toolName == "CalcSchedulePriority" && user.Role != 1))
            return System.Text.Json.JsonSerializer.Serialize(new { code = 1, msg = $"您（{user.Name}）无权查询此数据" });

        return null;
    }

    private string? ResolveSessionId(string? sessionId)
    {
        var s = (sessionId ?? "").Trim();
        if (!string.IsNullOrEmpty(s)) return s;
        var header = _http.HttpContext?.Request.Headers["X-Mcp-Session-Id"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(header) ? null : header.Trim();
    }

    private static string NormalizeChan(string? chanType)
    {
        var c = (chanType ?? "coze").Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(c) ? "coze" : c;
    }

    public static string HashSession(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
