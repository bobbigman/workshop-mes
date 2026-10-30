using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ahu.MicrosoftMes.Services;

public interface IReportIdentityDirectory
{
    Task<List<SysUser>> FindAsync(long? factoryId, string? name, string? phone);
    Task<SysUser?> GetAsync(long factoryId, long userId);
    Task<string> FactoryNameAsync(long factoryId);
    Task<string> FactoryCodeAsync(long factoryId) => Task.FromResult("");
}

public class ReportIdentityDirectory : IReportIdentityDirectory
{
    private readonly AppDbContext _db;
    public ReportIdentityDirectory(AppDbContext db) => _db = db;
    public Task<List<SysUser>> FindAsync(long? factoryId, string? name, string? phone)
    {
        var query = _db.Users.AsNoTracking().Where(u => (factoryId == null || u.FactoryId == factoryId) && u.Status == 1 &&
            u.UserType != UserTypeHelper.External && (u.Role == 1 || u.Role == 2 || u.Role == 3));
        query = phone != null ? query.Where(u => u.Phone != null && (phone.Length == 6 ? u.Phone.EndsWith(phone) : u.Phone == phone))
            : query.Where(u => u.Name == name);
        return query.OrderBy(u => u.FactoryId).ThenBy(u => u.Id).Take(101).ToListAsync();
    }
    public Task<SysUser?> GetAsync(long factoryId, long userId) => _db.Users.AsNoTracking()
        .FirstOrDefaultAsync(u => u.FactoryId == factoryId && u.Id == userId);
    public async Task<string> FactoryNameAsync(long factoryId) => await _db.Factories.AsNoTracking()
        .Where(f => f.Id == factoryId).Select(f => f.FactoryName).FirstOrDefaultAsync()
        ?? throw ThrowHelper.BizUser("当前工厂不存在，请检查MCP连接");
    public async Task<string> FactoryCodeAsync(long factoryId) => await _db.Factories.AsNoTracking()
        .Where(f => f.Id == factoryId).Select(f => f.FactoryCode).FirstOrDefaultAsync() ?? "";
}

public sealed record ReportLookup(long FactoryId, long UserId, string UserName, string FactoryName, DateTimeOffset ExpiresAt, long OriginFactoryId);
public sealed record ReportLoginGrant(long FactoryId, long UserId, string GrantId, DateTimeOffset ExpiresAt, long OriginFactoryId);
public sealed record ReportFactoryOption(int Number, long FactoryId, long? UserId, string FactoryName, string FactoryCode);

/// <summary>单进程短期授权；所有票据变更原子执行，状态不落库，重启失效。</summary>
public class ReportIdentityTickets
{
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _clock;
    private readonly object _sync = new();
    private static readonly object PasswordSync = new();
    private readonly string _ticketNamespace;
    public ReportIdentityTickets(IMemoryCache cache, TimeProvider? clock = null, string ticketNamespace = "report-identity")
        => (_cache, _clock, _ticketNamespace) = (cache, clock ?? TimeProvider.System, ticketNamespace);
    private DateTimeOffset Now => _clock.GetUtcNow();
    private static string RandomToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    private string Key(string prefix, string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length != 64 || !token.All(Uri.IsHexDigit)) throw ThrowHelper.BizUser("授权凭证无效，请重新验证");
        return _ticketNamespace + ":" + prefix + ":" + token;
    }
    public string Create(long factoryId, long userId, string name, string factoryName, long? originFactoryId = null)
    {
        var token = RandomToken();
        _cache.Set(Key("lookup", token), new Pending(new ReportLookup(factoryId, userId, name, factoryName, Now.AddMinutes(10), originFactoryId ?? factoryId)) { Version = Version(factoryId, userId) }, TimeSpan.FromMinutes(10));
        return token;
    }
    public string CreateFactorySelection(long originFactoryId, ReportFactoryOption[] options)
    {
        var token = RandomToken();
        _cache.Set(Key("factories", token), new FactorySelection(originFactoryId, options, Now.AddMinutes(10)), TimeSpan.FromMinutes(10));
        return token;
    }
    public ReportFactoryOption SelectFactory(string token, long originFactoryId, int number)
    {
        lock (_sync)
        {
            var key = Key("factories", token);
            if (!_cache.TryGetValue(key, out FactorySelection? selection) || selection == null || selection.ExpiresAt <= Now)
                throw ThrowHelper.BizUser("工厂选择已过期或已使用，请重新按姓名或手机号查找");
            if (selection.OriginFactoryId != originFactoryId) throw ThrowHelper.BizUser("工厂选择凭证不属于当前MCP入口");
            var option = selection.Options.FirstOrDefault(o => o.Number == number)
                ?? throw ThrowHelper.BizUser("工厂序号不在实际匹配列表中，请重新选择");
            _cache.Remove(key);
            return option;
        }
    }
    private Pending PendingFor(string token)
    {
        if (!_cache.TryGetValue(Key("lookup", token), out Pending? pending) || pending == null ||
            pending.Lookup.ExpiresAt <= Now || pending.Redeemed || pending.Attempts >= 5
            || pending.Version != Version(pending.Lookup.FactoryId, pending.Lookup.UserId))
            throw ThrowHelper.BizUser("验证链接已过期、已使用或已作废，请让机器人重新定位员工");
        return pending;
    }
    public ReportLookup Read(string token) { lock (_sync) return PendingFor(token).Lookup; }
    public string Approve(string token, long factoryId, long userId)
    {
        lock (_sync)
        {
            var pending = PendingFor(token);
            if (pending.Lookup.FactoryId != factoryId || pending.Lookup.UserId != userId)
                throw ThrowHelper.BizUser("当前登录账号与待验证员工或工厂不一致，请切换到本人的正确账号");
            pending.Code = RandomNumberGenerator.GetInt32(100_000_000).ToString("D8");
            pending.CodeExpires = Now.AddMinutes(5);
            pending.Lookup = pending.Lookup with { ExpiresAt = pending.CodeExpires };
            _cache.Set(Key("lookup", token), pending, TimeSpan.FromMinutes(5));
            return pending.Code;
        }
    }
    public (string Token, ReportLoginGrant Grant) Redeem(string token, long factoryId, string code)
    {
        lock (_sync)
        {
            var pending = PendingFor(token);
            if (pending.Lookup.OriginFactoryId != factoryId) throw ThrowHelper.BizUser("验证凭证不属于当前MCP入口");
            if (pending.Code == null) throw ThrowHelper.BizUser("请先打开验证页，登录本人账号并点击确认授权，再提供验证码");
            if (pending.CodeExpires <= Now) throw ThrowHelper.BizUser("验证码已过期，请重新打开验证页确认授权");
            if (string.IsNullOrEmpty(code) || !Regex.IsMatch(code, "^[0-9]{8}$") || !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(code), System.Text.Encoding.UTF8.GetBytes(pending.Code)))
            {
                pending.Attempts++;
                throw ThrowHelper.BizUser(pending.Attempts >= 5 ? "验证码错误已达5次，本次验证作废，请重新定位员工" : "验证码不正确，请核对验证页上的8位验证码");
            }
            var access = RandomToken();
            var grant = new ReportLoginGrant(pending.Lookup.FactoryId, pending.Lookup.UserId, Guid.NewGuid().ToString("N"), Now.AddMinutes(30), pending.Lookup.OriginFactoryId);
            _cache.Set(Key("grant", access), grant, TimeSpan.FromMinutes(30));
            _cache.Set(Key("grant-version", access), Version(grant.FactoryId, grant.UserId), TimeSpan.FromMinutes(30));
            pending.Redeemed = true;
            pending.Code = null;
            return (access, grant);
        }
    }
    public ReportLoginGrant Resolve(string token, long factoryId)
    {
        if (!_cache.TryGetValue(Key("grant", token), out ReportLoginGrant? grant) || grant == null || grant.ExpiresAt <= Now)
            throw ThrowHelper.BizUser("报工登录授权已失效，请重新验证身份");
        if (grant.OriginFactoryId != factoryId) throw ThrowHelper.BizUser("报工登录授权不属于当前MCP入口");
        if (!_cache.TryGetValue(Key("grant-version", token), out string? version) || version != Version(grant.FactoryId, grant.UserId))
            throw ThrowHelper.BizUser("授权已撤销，请重新验证身份。");
        return grant;
    }
    private string Version(long factoryId, long userId) => _cache.Get<string>($"{_ticketNamespace}:version:{factoryId}:{userId}") ?? "";
    public void Revoke(long factoryId, long userId)
    {
        lock (_sync) _cache.Set($"{_ticketNamespace}:version:{factoryId}:{userId}", Guid.NewGuid().ToString("N"));
    }
    public void BeginPasswordAttempt(long factoryId, long userId)
    {
        lock (PasswordSync)
        {
            var key = $"report-identity:password:{factoryId}:{userId}";
            if (_cache.TryGetValue($"report-identity:password:{factoryId}:{userId}", out int failures) && failures >= 5)
                throw ThrowHelper.BizUser("本账号授权密码错误次数过多，请5分钟后再试");
            _cache.Set(key, failures + 1, TimeSpan.FromMinutes(5));
        }
    }
    public void ClearPasswordAttempts(long factoryId, long userId)
    {
        lock (PasswordSync) _cache.Remove($"report-identity:password:{factoryId}:{userId}");
    }
    private sealed class Pending(ReportLookup lookup)
    {
        public ReportLookup Lookup { get; set; } = lookup;
        public string Version { get; init; } = "";
        public string? Code { get; set; }
        public DateTimeOffset CodeExpires { get; set; }
        public bool Redeemed { get; set; }
        public int Attempts { get; set; }
    }
    private sealed record FactorySelection(long OriginFactoryId, ReportFactoryOption[] Options, DateTimeOffset ExpiresAt);
}

public interface IReportIdentityService
{
    Task<object> IdentifyAsync(long factoryId, string? displayName, string? phone,
        string? factorySelectionToken = null, int? factoryNumber = null);
    Task<ReportLookup> InspectAsync(string lookupToken, long factoryId, long userId);
    Task<string> ApproveAsync(string lookupToken, long factoryId, long userId, string password);
    Task<object> VerifyAsync(long factoryId, string lookupToken, string code);
    Task<(ReportLoginGrant Grant, SysUser User)> ResolveAsync(long factoryId, string accessToken);
}

public class ReportIdentityService : IReportIdentityService
{
    private readonly IReportIdentityDirectory _directory;
    private readonly ReportIdentityTickets _tickets;
    private readonly IConfiguration _config;
    public ReportIdentityService(IReportIdentityDirectory directory, ReportIdentityTickets tickets, IConfiguration config)
        => (_directory, _tickets, _config) = (directory, tickets, config);

    public async Task<object> IdentifyAsync(long factoryId, string? displayName, string? phone,
        string? factorySelectionToken = null, int? factoryNumber = null)
    {
        if (factorySelectionToken != null || factoryNumber != null)
        {
            if (!string.IsNullOrWhiteSpace(displayName) || !string.IsNullOrWhiteSpace(phone) || factorySelectionToken == null || factoryNumber == null)
                throw ThrowHelper.BizUser("选择工厂时只传本次选择凭证和工厂序号，不混填姓名或手机号");
            var option = _tickets.SelectFactory(factorySelectionToken, factoryId, factoryNumber.Value);
            if (option.UserId == null) return new { state = "contact_admin", msg = "该工厂内同手机号对应多个账号，请联系管理员核对，不能自动选择员工" };
            var selected = await _directory.GetAsync(option.FactoryId, option.UserId.Value);
            EnsureActive(selected, option.FactoryId);
            return await LoginRequiredAsync(factoryId, selected!);
        }
        var name = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        var digits = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (name?.Length > 100) throw ThrowHelper.BizUser("姓名不能超过100字");
        if (digits != null && !Regex.IsMatch(digits, "^([0-9]{6}|1[0-9]{10})$"))
            throw ThrowHelper.BizUser("请提供手机号后6位，或完整11位手机号");
        if (name == null && digits == null) return new { state = "need_phone_suffix", msg = "请提供您的姓名或手机号后6位" };
        // 246：入口钥匙不替用户决定目标厂。这里只做有限身份定位，不返回任务或授予权限。
        var matches = await _directory.FindAsync(null, name, digits);
        if (matches.Count != 1)
        {
            if (digits?.Length == 11)
            {
                if (matches.Count == 0 || matches.Count > 100)
                    return new { state = "contact_admin", msg = "完整手机号未匹配有效员工或匹配项过多，请联系管理员核对员工资料" };
                var groups = matches.GroupBy(u => u.FactoryId).ToArray();
                if (groups.Length == 1)
                    return new { state = "contact_admin", msg = "同一工厂内完整手机号对应多个账号，请联系管理员核对，不能自动选择员工" };
                var options = new List<ReportFactoryOption>();
                foreach (var group in groups)
                    options.Add(new ReportFactoryOption(options.Count + 1, group.Key, group.Count() == 1 ? group.Single().Id : null,
                        await _directory.FactoryNameAsync(group.Key), await _directory.FactoryCodeAsync(group.Key)));
                var token = _tickets.CreateFactorySelection(factoryId, options.ToArray());
                var duplicateNames = options.GroupBy(o => o.FactoryName).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
                return new { state = "select_factory", factorySelectionToken = token, expireMinutes = 10,
                    factories = options.Select(o => new { number = o.Number, factoryName = o.FactoryName,
                        factoryCode = duplicateNames.Contains(o.FactoryName) ? o.FactoryCode : null }),
                    msg = "完整手机号匹配到多个工厂，请按实际列表选择本次报工的工厂序号" };
            }
            if (digits != null)
                return new { state = "need_full_phone", msg = "手机号后6位未唯一匹配，请提供完整11位手机号" };
            return new { state = "need_phone_suffix", msg = "姓名未唯一匹配，请提供手机号后6位" };
        }
        var user = matches[0];
        EnsureActive(user, user.FactoryId);
        return await LoginRequiredAsync(factoryId, user);
    }
    private async Task<object> LoginRequiredAsync(long originFactoryId, SysUser user)
    {
        var factoryName = await _directory.FactoryNameAsync(user.FactoryId);
        var baseUrl = (_config["Mcp:AuthBaseUrl"] ?? "").Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl)) baseUrl = (_config["Instance:PublicBaseUrl"] ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.IsLoopback ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw ThrowHelper.BizUser("请管理员将Mcp:AuthBaseUrl配置为当前手机可访问的HTTPS地址，不能使用localhost");
        var token = _tickets.Create(user.FactoryId, user.Id, user.Name, factoryName, originFactoryId);
        return new { state = "login_required", userName = user.Name, factoryName, lookupToken = token,
            authorizationUrl = $"{baseUrl}/#/h5/report-authorize?t={token}", expireMinutes = 10,
            msg = "已定位员工，请打开验证链接，登录本人账号并确认授权，再把页面上的8位验证码发给机器人。不要在聊天中发送密码。" };
    }
    public async Task<ReportLookup> InspectAsync(string lookupToken, long factoryId, long userId)
    {
        var lookup = _tickets.Read(lookupToken);
        if (lookup.FactoryId != factoryId || lookup.UserId != userId)
            throw ThrowHelper.BizUser("当前登录账号与待验证员工或工厂不一致，请切换到本人的正确账号");
        EnsureActive(await _directory.GetAsync(factoryId, userId), factoryId);
        return lookup;
    }
    public async Task<string> ApproveAsync(string lookupToken, long factoryId, long userId, string password)
    {
        await InspectAsync(lookupToken, factoryId, userId);
        _tickets.BeginPasswordAttempt(factoryId, userId);
        var user = await _directory.GetAsync(factoryId, userId);
        if (string.IsNullOrEmpty(password) || user == null || !PasswordHelper.Verify(password, user.Password))
        {
            throw ThrowHelper.BizUser("登录密码不正确，请仅在本验证页核对密码，不要发给机器人");
        }
        _tickets.ClearPasswordAttempts(factoryId, userId);
        return _tickets.Approve(lookupToken, factoryId, userId);
    }
    public async Task<object> VerifyAsync(long factoryId, string lookupToken, string code)
    {
        var lookup = _tickets.Read(lookupToken);
        if (lookup.OriginFactoryId != factoryId) throw ThrowHelper.BizUser("验证凭证不属于当前MCP入口");
        var user = await _directory.GetAsync(lookup.FactoryId, lookup.UserId);
        EnsureActive(user, lookup.FactoryId);
        var result = _tickets.Redeem(lookupToken, factoryId, code);
        return new { reportAccessToken = result.Token, userName = user!.Name, expiresAt = result.Grant.ExpiresAt,
            expireMinutes = 30, msg = "身份验证成功；后续报工工具携带reportAccessToken，不向用户展示凭证，仍须预览与确认。" };
    }
    public async Task<(ReportLoginGrant Grant, SysUser User)> ResolveAsync(long factoryId, string accessToken)
    {
        var grant = _tickets.Resolve(accessToken, factoryId);
        var user = await _directory.GetAsync(grant.FactoryId, grant.UserId);
        EnsureActive(user, grant.FactoryId);
        return (grant, user!);
    }
    private static void EnsureActive(SysUser? user, long factoryId)
    {
        if (user == null || user.FactoryId != factoryId || user.Status != 1 || user.UserType == UserTypeHelper.External || user.Role is not (1 or 2 or 3))
            throw ThrowHelper.BizUser("该员工账号不存在、已停用或不可登录，请联系管理员");
    }
}
