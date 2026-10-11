using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;

namespace ahu.MicrosoftMes.Mcp;

/// <summary>MCP：工人自报手机号/账号/姓名 → 专属免密报工链接（docs/215）。</summary>
public interface IMcpReportLinkService
{
    Task<string> GetReportLinkByIdentityAsync(string? identity, CancellationToken ct = default);
}

public class McpReportLinkService : IMcpReportLinkService
{
    private static readonly Regex PhoneRe = new(@"^1\d{10}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppDbContext _db;
    private readonly IMcpRequestContext _ctx;
    private readonly IUserService _users;
    private readonly IConfiguration _config;
    private readonly InstanceOptions _instance;

    public McpReportLinkService(
        AppDbContext db,
        IMcpRequestContext ctx,
        IUserService users,
        IConfiguration config,
        IOptions<InstanceOptions> instance)
    {
        _db = db;
        _ctx = ctx;
        _users = users;
        _config = config;
        _instance = instance.Value;
    }

    public async Task<string> GetReportLinkByIdentityAsync(string? identity, CancellationToken ct = default)
    {
        var factoryId = _ctx.RequireFactoryId(nameof(GetReportLinkByIdentityAsync));
        var raw = (identity ?? "").Trim();
        if (string.IsNullOrEmpty(raw))
            return Fail("请发您的手机号、登录账号或姓名");

        // 与 MCP 授权页同源：优先 Mcp:AuthBaseUrl（cpolar/公网随 MCP 改），勿写死；本机地址绝不发给手机
        var baseUrl = ResolvePhoneReachableBaseUrl();
        if (string.IsNullOrEmpty(baseUrl))
            return Fail("未配置手机可打开的地址。请把 Mcp:AuthBaseUrl 配成与 MCP 相同的 cpolar/公网地址后重启（勿用 localhost）");

        List<SysUser> hits;
        string kind;
        if (PhoneRe.IsMatch(raw))
        {
            kind = "手机号";
            hits = await _db.Users.AsNoTracking()
                .Where(u => u.FactoryId == factoryId && u.Phone == raw)
                .OrderBy(u => u.Id)
                .ToListAsync(ct);
        }
        else
        {
            var byAccount = await _db.Users.AsNoTracking()
                .Where(u => u.FactoryId == factoryId && u.Account == raw)
                .OrderBy(u => u.Id)
                .ToListAsync(ct);
            if (byAccount.Count > 0)
            {
                kind = "登录账号";
                hits = byAccount;
            }
            else
            {
                kind = "姓名";
                hits = await _db.Users.AsNoTracking()
                    .Where(u => u.FactoryId == factoryId && u.Name == raw)
                    .OrderBy(u => u.Id)
                    .ToListAsync(ct);
            }
        }

        if (hits.Count == 0)
            return Fail($"未找到该{kind}的员工，请核对手机号/登录账号/姓名，或联系管理员在用户管理里录入");

        if (hits.Count > 1)
        {
            var list = string.Join("、", hits.Select(u => $"{u.Name}({u.Account})"));
            return Fail($"找到多人：{list}，请发手机号或登录账号");
        }

        var user = hits[0];
        if (user.Status == 0)
            return Fail("该账号已停用，请联系管理员");

        var qr = await _users.GetOrCreateLoginQrAsync(user.Id, factoryId);
        if (qr.Code != 0 || qr.Data == null || string.IsNullOrEmpty(qr.Data.Code))
            return Fail(string.IsNullOrEmpty(qr.Msg) ? "生成报工链接失败，请稍后重试" : qr.Msg);

        var link = $"{baseUrl}/#/login?pq={Uri.EscapeDataString(qr.Data.Code)}";
        // cardText 内嵌完整 URL：企微机器人若只抄文案、改写 link 域名，用户仍能拿到可打开地址
        var cardText = $"{user.Name}师傅，点这里报工：{link}";
        return JsonSerializer.Serialize(new
        {
            code = 0,
            msg = "请把 link 原样发给用户（完整 https），禁止改成 localhost 或其它域名",
            name = user.Name,
            account = user.Account,
            link,
            cardText
        }, JsonOpts);
    }

    private static string Fail(string msg)
        => JsonSerializer.Serialize(new { code = 1, msg }, JsonOpts);

    /// <summary>
    /// 手机/企微外链基址：跟 MCP 公网入口（AuthBaseUrl）；空时才回落 Instance:PublicBaseUrl。
    /// 不回落 AuthLocalBaseUrl（那是本机自测，手机会 CONNECTION_REFUSED）。
    /// </summary>
    private string? ResolvePhoneReachableBaseUrl()
    {
        foreach (var candidate in new[]
                 {
                     (_config["Mcp:AuthBaseUrl"] ?? "").Trim().TrimEnd('/'),
                     (_instance.PublicBaseUrl ?? "").Trim().TrimEnd('/')
                 })
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            if (IsLoopbackBaseUrl(candidate)) continue;
            return candidate;
        }
        return null;
    }

    private static bool IsLoopbackBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host;
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
               || host == "127.0.0.1"
               || host == "::1"
               || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }
}
