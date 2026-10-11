using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IAuthService
{
    Task<ApiResult<LoginResultDto>> LoginAsync(LoginDto dto);
    Task<ApiResult<LoginResultDto>> SelectFactoryAsync(SelectFactoryDto dto);
    Task<ApiResult<LoginResultDto>> LoginByPhoneAsync(PhoneLoginDto dto);
    /// <summary>个人免密二维码登录（docs/150）：签发 30 天 JWT</summary>
    Task<ApiResult<LoginResultDto>> LoginByQrAsync(LoginByQrDto dto);
    Task<ApiResult<object?>> ChangePasswordAsync(ChangePasswordDto dto, long userId);
}

public class LoginDto
{
    /// <summary>已废弃（docs/151）：登录不再前端选厂；保留字段仅为兼容旧客户端忽略。</summary>
    public string FactoryCode { get; set; } = "";
    public string Account { get; set; } = "";
    public string Password { get; set; } = "";
}

public class SelectFactoryDto
{
    public string SelectToken { get; set; } = "";
    public string FactoryCode { get; set; } = "";
}

public class PhoneLoginDto
{
    public string Phone { get; set; } = "";
    public string SmsCode { get; set; } = "";
}

public class LoginByQrDto
{
    /// <summary>个人免密码密钥（URL 查询参数 pq）</summary>
    public string Code { get; set; } = "";
}

public class ChangePasswordDto
{
    public string OldPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public class FactoryCandidateDto
{
    public string FactoryCode { get; set; } = "";
    public string FactoryName { get; set; } = "";
}

public class LoginResultDto
{
    /// <summary>true=需同页选厂，此时 Token 为空，用 SelectToken + Candidates（docs/151）</summary>
    public bool NeedSelectFactory { get; set; }
    public string? SelectToken { get; set; }
    public List<FactoryCandidateDto>? Candidates { get; set; }

    public string Token { get; set; } = "";
    public long UserId { get; set; }
    public string Name { get; set; } = "";
    public byte Role { get; set; }
    public string FactoryCode { get; set; } = "";
    public string FactoryName { get; set; } = "";
    /// <summary>有效授权档（含云激活/宽限降级后）：trial / enterprise / flagship</summary>
    public string LicenseTier { get; set; } = "trial";
    /// <summary>disabled / valid / grace / expired</summary>
    public string LicenseStatus { get; set; } = "disabled";
    public string? LicenseMessage { get; set; }
    /// <summary>工人手机视角：1=全车间可见(默认) 2=只看我的任务（docs/200）</summary>
    public byte WorkerViewMode { get; set; } = 1;
}

public class AuthService : IAuthService
{
    /// <summary>个人免密二维码登录 JWT 有效期（小时）；账号密码仍用 Jwt:ExpireHours</summary>
    public const int PersonalQrExpireHours = 30 * 24;

    private static readonly string SelectTokenCachePrefix = "select-factory:jti:";

    private readonly AppDbContext _db;
    private readonly JwtOptions _jwt;
    private readonly InstanceOptions _instance;
    private readonly ILicenseCloudService _licenseCloud;
    private readonly DeploymentOptions _deployment;
    private readonly TrialCleanupOptions _trialCleanup;
    private readonly IMemoryCache _cache;

    public AuthService(
        AppDbContext db,
        IOptions<JwtOptions> jwt,
        IOptions<InstanceOptions> instance,
        ILicenseCloudService licenseCloud,
        IOptions<DeploymentOptions> deployment,
        IOptions<TrialCleanupOptions> trialCleanup,
        IMemoryCache cache)
    {
        _db = db;
        _jwt = jwt.Value;
        _instance = instance.Value;
        _licenseCloud = licenseCloud;
        _deployment = deployment.Value;
        _trialCleanup = trialCleanup.Value;
        _cache = cache;
    }

    /// <summary>docs/151：按账号+BCrypt 验密，不带 factory；1 厂直发 JWT / ≥2 厂发 selectToken</summary>
    public async Task<ApiResult<LoginResultDto>> LoginAsync(LoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Account))
            throw ThrowHelper.BizUser("账号或密码错误");
        if (string.IsNullOrEmpty(dto.Password))
            throw ThrowHelper.BizUser("账号或密码错误");

        var account = dto.Account.Trim();
        var users = await _db.Users.AsNoTracking()
            .Where(u => u.Account == account && u.Status == 1)
            .ToListAsync();

        var matched = new List<(SysUser User, SysFactory Factory)>();
        foreach (var user in users)
        {
            if (!PasswordHelper.Verify(dto.Password, user.Password))
                continue;
            var factory = await _db.Factories.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == user.FactoryId);
            if (factory == null)
                continue;
            matched.Add((user, factory));
        }

        if (matched.Count == 0)
            throw ThrowHelper.BizUser("账号或密码错误");

        // docs/161：外部供应商不进选厂列表；若密码对但全是外部 → 明确拒登
        var loginable = matched.Where(m => m.User.UserType != UserTypeHelper.External).ToList();
        if (loginable.Count == 0)
            throw ThrowHelper.BizUser(UserTypeHelper.ExternalLoginDeniedMsg);

        if (loginable.Count == 1)
        {
            var (user, factory) = loginable[0];
            return await IssueLoginAsync(user, factory, _jwt.ExpireHours);
        }

        var claims = loginable
            .Select(m => new SelectFactoryCandidateClaim
            {
                FactoryCode = m.Factory.FactoryCode,
                UserId = m.User.Id
            })
            .ToList();
        var (selectToken, jti) = JwtHelper.CreateSelectFactoryToken(_jwt, claims);
        _cache.Set(
            SelectTokenCachePrefix + jti,
            1,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(JwtHelper.SelectFactoryExpireMinutes)
            });

        return ApiResult<LoginResultDto>.Ok(new LoginResultDto
        {
            NeedSelectFactory = true,
            SelectToken = selectToken,
            Candidates = loginable
                .Select(m => new FactoryCandidateDto
                {
                    FactoryCode = m.Factory.FactoryCode,
                    FactoryName = m.Factory.FactoryName
                })
                .OrderBy(c => c.FactoryCode)
                .ToList()
        });
    }

    /// <summary>docs/151：用短时 selectToken 换正式 JWT；伪造厂码 / 过期 / 复用一律拒</summary>
    public async Task<ApiResult<LoginResultDto>> SelectFactoryAsync(SelectFactoryDto dto)
    {
        var factoryCode = (dto.FactoryCode ?? "").Trim();
        if (string.IsNullOrEmpty(factoryCode))
            throw ThrowHelper.BizUser("请选择工厂");

        var (jti, candidates) = JwtHelper.ValidateSelectFactoryToken(_jwt, dto.SelectToken ?? "");
        var cacheKey = SelectTokenCachePrefix + jti;
        if (!_cache.TryGetValue(cacheKey, out _))
            throw ThrowHelper.BizUser("选厂凭证无效或已过期，请重新登录");
        _cache.Remove(cacheKey);

        var hit = candidates.FirstOrDefault(c =>
            string.Equals(c.FactoryCode, factoryCode, StringComparison.OrdinalIgnoreCase));
        if (hit == null || hit.UserId <= 0)
            throw ThrowHelper.BizUser("无权进入该工厂，请重新登录");

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == hit.UserId && u.Status == 1)
            ?? throw ThrowHelper.BizUser("选厂凭证无效或已过期，请重新登录");

        var factory = await _db.Factories.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == user.FactoryId)
            ?? throw ThrowHelper.BizUser("无权进入该工厂，请重新登录");

        if (!string.Equals(factory.FactoryCode, factoryCode, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(factory.FactoryCode, hit.FactoryCode, StringComparison.OrdinalIgnoreCase))
            throw ThrowHelper.BizUser("无权进入该工厂，请重新登录");

        return await IssueLoginAsync(user, factory, _jwt.ExpireHours);
    }

    public async Task<ApiResult<LoginResultDto>> LoginByQrAsync(LoginByQrDto dto)
    {
        var code = (dto.Code ?? "").Trim();
        if (string.IsNullOrEmpty(code) || code.Length > 64)
            throw ThrowHelper.BizUser("个人码无效或账号已停用，请使用账号密码登录");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.LoginQrSecret == code && u.Status == 1);
        if (user == null)
            throw ThrowHelper.BizUser("个人码无效或账号已停用，请使用账号密码登录");

        var factory = await _db.Factories.FirstOrDefaultAsync(f => f.Id == user.FactoryId)
            ?? throw ThrowHelper.BizUser("个人码无效或账号已停用，请使用账号密码登录");

        return await IssueLoginAsync(user, factory, PersonalQrExpireHours);
    }

    private async Task<ApiResult<LoginResultDto>> IssueLoginAsync(SysUser user, SysFactory factory, int expireHours)
    {
        // docs/161：外部供应商 JWT 签发前硬拒（账号密码 / 选厂 / 个人码共用）
        UserTypeHelper.EnsureCanLogin(user.UserType);

        // docs/90：体验到期/已清场拦截（先于授权云，绝不误伤正式/私有）
        RejectIfTrialExpired(factory);

        await _licenseCloud.RefreshAsync(factory.Id);
        var lic = await _licenseCloud.GetStatusAsync(factory.Id);

        var token = JwtHelper.CreateToken(_jwt, user.Id, user.FactoryId, user.Role, user.Name, expireHours);
        var viewMode = await _db.WorkerViewSettings.AsNoTracking()
            .Where(x => x.FactoryId == factory.Id)
            .Select(x => (byte?)x.WorkerViewMode)
            .FirstOrDefaultAsync();
        return ApiResult<LoginResultDto>.Ok(new LoginResultDto
        {
            NeedSelectFactory = false,
            Token = token,
            UserId = user.Id,
            Name = user.Name,
            Role = user.Role,
            FactoryCode = factory.FactoryCode,
            FactoryName = factory.FactoryName,
            LicenseTier = lic.EffectiveTier,
            LicenseStatus = lic.Status,
            LicenseMessage = string.IsNullOrEmpty(lic.Message) ? null : lic.Message,
            WorkerViewMode = viewMode == 2 ? (byte)2 : (byte)1
        });
    }

    private void RejectIfTrialExpired(SysFactory factory)
    {
        if (_deployment.Private)
            return;
        if (!string.Equals(factory.LicenseTier, "trial", StringComparison.OrdinalIgnoreCase))
            return;
        if (factory.TrialCleanedAtUtc.HasValue)
            throw ThrowHelper.BizUser("体验账套已到期，请联系部署方转正式版或重新开通体验");
        if (!factory.TrialExpiresAtUtc.HasValue)
            return;

        var grace = Math.Max(0, _trialCleanup.GraceMinutes);
        var deadline = factory.TrialExpiresAtUtc.Value.AddMinutes(grace);
        if (DateTime.UtcNow >= deadline)
            throw ThrowHelper.BizUser("体验账套已到期，请联系部署方转正式版或重新开通体验");
    }

    public Task<ApiResult<LoginResultDto>> LoginByPhoneAsync(PhoneLoginDto dto)
    {
        if (!_instance.AllowPhoneLogin)
            throw ThrowHelper.BizUser("手机号登录暂未开放");

        // 即便开启，仍禁止固定万能验证码；未接短信服务前直接不可用
        throw ThrowHelper.BizUser("手机号登录暂未开放");
    }

    public async Task<ApiResult<object?>> ChangePasswordAsync(ChangePasswordDto dto, long userId)
    {
        PasswordHelper.EnsureComplexity(dto.NewPassword, nameof(ChangePasswordAsync));

        var user = await _db.Users.FindAsync(userId)
            ?? throw ThrowHelper.Biz(nameof(ChangePasswordAsync), "用户不存在");

        if (!PasswordHelper.Verify(dto.OldPassword, user.Password))
            throw ThrowHelper.Biz(nameof(ChangePasswordAsync), "原密码错误");

        user.Password = PasswordHelper.Hash(dto.NewPassword);
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }
}
