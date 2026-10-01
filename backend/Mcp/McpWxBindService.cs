using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Mcp;

/// <summary>MCP 手机号直绑认人（docs/97）。</summary>
public interface IMcpWxBindService
{
    Task<string> BindAsync(string phone, string? chanType, string? sessionId, CancellationToken ct = default);
    Task<string> UnbindAsync(string? chanType, string? sessionId, CancellationToken ct = default);
    /// <summary>未绑定返回 null；已绑定返回用户。缺会话身份抛错。</summary>
    Task<SysUser?> ResolveBoundUserAsync(string? chanType, string? sessionId, CancellationToken ct = default);
    /// <summary>非演示模式：必须已绑定，否则返回提示 JSON；演示模式跳过。</summary>
    Task<string?> RequireBoundOrHintAsync(string? chanType, string? sessionId, string toolName, CancellationToken ct = default);
}

public class McpWxBindService : IMcpWxBindService
{
    private static readonly Regex PhoneRe = new(@"^1\d{10}$", RegexOptions.Compiled);
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

    public async Task<string> BindAsync(string phone, string? chanType, string? sessionId, CancellationToken ct = default)
    {
        var factoryId = _ctx.RequireFactoryId(nameof(BindAsync));
        var phoneTrim = (phone ?? "").Trim();
        if (!PhoneRe.IsMatch(phoneTrim))
            return """{"code":1,"msg":"手机号格式不对"}""";

        var chan = NormalizeChan(chanType);
        var rawSession = ResolveSessionId(sessionId);
        if (string.IsNullOrEmpty(rawSession))
            return """{"code":1,"msg":"缺少会话身份，无法绑定"}""";

        var openHash = HashSession(rawSession);
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.FactoryId == factoryId && u.Phone == phoneTrim, ct);
        if (user == null)
            return """{"code":1,"msg":"该手机号在本厂无账号"}""";
        if (user.Status == 0)
            return """{"code":1,"msg":"账号已停用"}""";

        var existing = await _db.WxBinds
            .FirstOrDefaultAsync(b =>
                b.FactoryId == factoryId && b.ChanType == chan && b.WxOpenid == openHash && b.Status == 1, ct);
        if (existing != null)
        {
            var boundName = await _db.Users.AsNoTracking()
                .Where(u => u.Id == existing.UserId)
                .Select(u => u.Name)
                .FirstOrDefaultAsync(ct) ?? "?";
            return $"{{\"code\":1,\"msg\":\"已绑定账号：{boundName}，如需换账号先解绑\"}}";
        }

        _db.WxBinds.Add(new SysWxBind
        {
            FactoryId = factoryId,
            ChanType = chan,
            WxOpenid = openHash,
            UserId = user.Id,
            Status = 1,
            BoundAt = DateTime.Now
        });
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "MCP 绑定成功 factory={FactoryId} chan={Chan} openPrefix={Prefix} userId={UserId} name={Name}",
            factoryId, chan, openHash[..Math.Min(8, openHash.Length)], user.Id, user.Name);

        return $"{{\"code\":0,\"msg\":\"绑定成功：{user.Name}\"}}";
    }

    public async Task<string> UnbindAsync(string? chanType, string? sessionId, CancellationToken ct = default)
    {
        var factoryId = _ctx.RequireFactoryId(nameof(UnbindAsync));
        var chan = NormalizeChan(chanType);
        var rawSession = ResolveSessionId(sessionId);
        if (string.IsNullOrEmpty(rawSession))
            return """{"code":1,"msg":"缺少会话身份"}""";

        var openHash = HashSession(rawSession);
        var row = await _db.WxBinds
            .FirstOrDefaultAsync(b =>
                b.FactoryId == factoryId && b.ChanType == chan && b.WxOpenid == openHash && b.Status == 1, ct);
        if (row == null)
            return """{"code":0,"msg":"当前未绑定"}""";

        row.Status = 0;
        row.UnboundAt = DateTime.Now;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "MCP 解绑 factory={FactoryId} chan={Chan} openPrefix={Prefix} userId={UserId}",
            factoryId, chan, openHash[..Math.Min(8, openHash.Length)], row.UserId);

        return """{"code":0,"msg":"已解绑，下次需重新绑定"}""";
    }

    public async Task<SysUser?> ResolveBoundUserAsync(string? chanType, string? sessionId, CancellationToken ct = default)
    {
        var factoryId = _ctx.RequireFactoryId(nameof(ResolveBoundUserAsync));
        var rawSession = ResolveSessionId(sessionId);
        if (string.IsNullOrEmpty(rawSession))
            throw ThrowHelper.BizUser("缺少会话身份，请先绑定：报您的手机号");

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
        if (_config.GetValue("Mcp:DemoSkipAuth", false))
            return null;

        var user = await ResolveBoundUserAsync(chanType, sessionId, ct);
        if (user == null)
            return """{"code":1,"msg":"请先绑定：报您的手机号"}""";

        // 工资工具额外校验：仅管理员（role=1）或财务名单账号
        if (string.Equals(toolName, "QuerySalary", StringComparison.OrdinalIgnoreCase))
        {
            var financeAccounts = (_config["Mcp:FinanceKingdeeAccounts"] ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var ok = user.Role == 1
                || financeAccounts.Any(a => string.Equals(a, user.Account, StringComparison.OrdinalIgnoreCase));
            if (!ok)
                return $"{{\"code\":1,\"msg\":\"您（{user.Name}）无权查询此数据\"}}";
        }

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
