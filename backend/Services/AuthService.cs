using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IAuthService
{
    Task<ApiResult<LoginResultDto>> LoginAsync(LoginDto dto);
    Task<ApiResult<LoginResultDto>> LoginByPhoneAsync(PhoneLoginDto dto);
    Task<ApiResult<object?>> ChangePasswordAsync(ChangePasswordDto dto, long userId);
}

public class LoginDto
{
    public string FactoryCode { get; set; } = "";
    public string Account { get; set; } = "";
    public string Password { get; set; } = "";
}

public class PhoneLoginDto
{
    public string Phone { get; set; } = "";
    public string SmsCode { get; set; } = "";
}

public class ChangePasswordDto
{
    public string OldPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public class LoginResultDto
{
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
}

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly JwtOptions _jwt;
    private readonly InstanceOptions _instance;
    private readonly ILicenseCloudService _licenseCloud;
    private readonly DeploymentOptions _deployment;
    private readonly TrialCleanupOptions _trialCleanup;

    public AuthService(
        AppDbContext db,
        IOptions<JwtOptions> jwt,
        IOptions<InstanceOptions> instance,
        ILicenseCloudService licenseCloud,
        IOptions<DeploymentOptions> deployment,
        IOptions<TrialCleanupOptions> trialCleanup)
    {
        _db = db;
        _jwt = jwt.Value;
        _instance = instance.Value;
        _licenseCloud = licenseCloud;
        _deployment = deployment.Value;
        _trialCleanup = trialCleanup.Value;
    }

    public async Task<ApiResult<LoginResultDto>> LoginAsync(LoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Account))
            throw ThrowHelper.BizUser("账号不能为空");

        // docs/52：多账套——优先前端传入的 FactoryCode；为空回退 Instance:FactoryCode
        var requested = (dto.FactoryCode ?? "").Trim();
        var factoryCode = string.IsNullOrWhiteSpace(requested)
            ? (_instance.FactoryCode?.Trim() ?? "")
            : requested;

        if (string.IsNullOrWhiteSpace(factoryCode))
            throw ThrowHelper.BizUser("工厂代码缺失，请联系管理员");

        var factory = await _db.Factories.FirstOrDefaultAsync(f => f.FactoryCode == factoryCode)
            ?? throw ThrowHelper.BizUser("账套不存在，请联系管理员");

        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.FactoryId == factory.Id && u.Account == dto.Account && u.Status == 1)
            ?? throw ThrowHelper.BizUser("账号不存在或已停用");

        if (!PasswordHelper.Verify(dto.Password, user.Password))
            throw ThrowHelper.BizUser("账号或密码错误");

        // docs/90：体验到期/已清场拦截（先于授权云，绝不误伤正式/私有）
        RejectIfTrialExpired(factory);

        await _licenseCloud.RefreshAsync(factory.Id);
        var lic = await _licenseCloud.GetStatusAsync(factory.Id);

        var token = JwtHelper.CreateToken(_jwt, user.Id, user.FactoryId, user.Role, user.Name);
        return ApiResult<LoginResultDto>.Ok(new LoginResultDto
        {
            Token = token,
            UserId = user.Id,
            Name = user.Name,
            Role = user.Role,
            FactoryCode = factory.FactoryCode,
            FactoryName = factory.FactoryName,
            LicenseTier = lic.EffectiveTier,
            LicenseStatus = lic.Status,
            LicenseMessage = string.IsNullOrEmpty(lic.Message) ? null : lic.Message
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
