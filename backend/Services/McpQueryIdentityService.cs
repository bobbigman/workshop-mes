using System.Security.Cryptography;
using System.Text;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ahu.MicrosoftMes.Services;

/// <summary>与报工票据共用验证算法，使用独立命名空间，权限不互换。</summary>
public sealed class QueryIdentityTickets(IMemoryCache cache) : ReportIdentityTickets(cache, ticketNamespace: "mcp-query-identity");

public sealed class McpQueryIdentityService(AppDbContext db, QueryIdentityTickets tickets, IConfiguration config)
{
    public async Task<object> StartAsync(long factoryId, string phone, CancellationToken ct = default)
    {
        var users = await db.Users.AsNoTracking().Where(u => u.FactoryId == factoryId && u.Phone == phone
            && u.Status == 1 && u.UserType != UserTypeHelper.External && (u.Role == 1 || u.Role == 2 || u.Role == 3))
            .Take(2).ToListAsync(ct);
        if (string.IsNullOrWhiteSpace(phone) || !System.Text.RegularExpressions.Regex.IsMatch(phone, "^1[0-9]{10}$") || users.Count != 1)
            throw ThrowHelper.BizUser("无法唯一定位本厂账号，请核对手机号或联系管理员。");
        var user = await CurrentAccountAccess.RequireAsync(db, users[0].Id, factoryId, false, ct);
        var baseUrl = (config["Mcp:AuthBaseUrl"] ?? "").Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = (config["Instance:PublicBaseUrl"] ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.IsLoopback
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw ThrowHelper.BizUser("请管理员配置可访问的HTTPS验证地址。");
        var name = await db.Factories.Where(f => f.Id == factoryId).Select(f => f.FactoryName).SingleAsync(ct);
        var token = tickets.Create(factoryId, user.Id, user.Name, name);
        return new { state = "login_required", lookupToken = token, authorizationUrl = $"{baseUrl}/#/h5/report-authorize?purpose=query&t={token}",
            expireMinutes = 10, msg = "请打开链接，用本人账号登录并确认仅查询授权，将8位验证码返回机器人；不要在聊天发送密码。" };
    }

    public async Task<ReportLookup> InspectAsync(string token, long factoryId, long userId, CancellationToken ct = default)
    {
        await CurrentAccountAccess.RequireAsync(db, userId, factoryId, false, ct);
        var lookup = tickets.Read(token);
        if (lookup.FactoryId != factoryId || lookup.UserId != userId)
            throw ThrowHelper.BizUser("当前账号或工厂与验证请求不一致，请登录本人正确账号。");
        return lookup;
    }

    public async Task<string> ApproveAsync(string token, long factoryId, long userId, string password, CancellationToken ct = default)
    {
        await InspectAsync(token, factoryId, userId, ct);
        tickets.BeginPasswordAttempt(factoryId, userId);
        var user = await CurrentAccountAccess.RequireAsync(db, userId, factoryId, false, ct);
        if (string.IsNullOrEmpty(password) || !PasswordHelper.Verify(password, user.Password))
            throw ThrowHelper.BizUser("密码不正确，请只在网页核对本人的登录密码。");
        tickets.ClearPasswordAttempts(factoryId, userId);
        return tickets.Approve(token, factoryId, userId);
    }

    public async Task<object> VerifyAsync(long factoryId, string lookupToken, string code, CancellationToken ct = default)
    {
        var lookup = tickets.Read(lookupToken);
        if (lookup.FactoryId != factoryId || lookup.OriginFactoryId != factoryId)
            throw ThrowHelper.BizUser("验证请求不属于当前工厂。");
        await CurrentAccountAccess.RequireAsync(db, lookup.UserId, factoryId, false, ct);
        var result = tickets.Redeem(lookupToken, factoryId, code);
        return new { queryAccessToken = result.Token, expiresAt = result.Grant.ExpiresAt, expireMinutes = 30,
            msg = "仅查询授权成功；凭证不得展示或朗读，不授予报工、维护或跨厂权限。" };
    }

    public async Task<SysUser> ResolveAsync(long factoryId, string token, CancellationToken ct = default)
    {
        var grant = tickets.Resolve(token, factoryId);
        if (grant.FactoryId != factoryId) throw ThrowHelper.BizUser("查询授权不属于当前工厂。");
        return await CurrentAccountAccess.RequireAsync(db, grant.UserId, factoryId, false, ct);
    }

    public void Revoke(long factoryId, long userId) => tickets.Revoke(factoryId, userId);

    // 只有受控网关持有的证明才能使平台头成为查询身份；厂钥匙本身不能证明个人身份。
    public static bool HasTrustedIdentity(HttpRequest? request, IConfiguration config)
    {
        var key = config["Mcp:Identity:TrustedProxyKey"];
        var proof = request?.Headers["X-Mcp-Identity-Key"].ToString();
        return config.GetValue("Mcp:Identity:Enabled", false) && key?.Length >= 32 && !string.IsNullOrEmpty(proof)
            && proof.Length <= 1024 && CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(key)), SHA256.HashData(Encoding.UTF8.GetBytes(proof)));
    }
}
