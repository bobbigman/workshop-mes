using ahu.MicrosoftMes.Common;

namespace ahu.MicrosoftMes.Services;

/// <summary>金蝶账号验证结果</summary>
public class KingdeeUserInfo
{
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsFinance { get; set; }
}

/// <summary>
/// 金蝶账号验证（M3）。验证「金蝶账号 + 密码」是否有效，返回金蝶用户身份与角色。
/// 金蝶云星空没有标准 OAuth2 授权码回调，现实路径是「H5 输入账号密码 → 服务端代验」。
/// </summary>
public interface IKingdeeAuthService
{
    Task<KingdeeUserInfo> VerifyAsync(string account, string password);
}

/// <summary>
/// 演示期占位实现：任意非空账号密码都视为验证通过。
/// 【TODO(M3)】接入金蝶云星空真实验证（WebAPI 登录接口），替换本类。
/// 是否财务角色由配置 Mcp:FinanceKingdeeAccounts（逗号分隔的金蝶账号）决定。
/// </summary>
public class FakeKingdeeAuthService : IKingdeeAuthService
{
    private readonly IConfiguration _config;

    public FakeKingdeeAuthService(IConfiguration config) => _config = config;

    public Task<KingdeeUserInfo> VerifyAsync(string account, string password)
    {
        account = (account ?? "").Trim();
        if (string.IsNullOrEmpty(account))
            throw ThrowHelper.Biz(nameof(VerifyAsync), "金蝶账号不能为空");
        if (string.IsNullOrEmpty(password))
            throw ThrowHelper.Biz(nameof(VerifyAsync), "金蝶密码不能为空");

        var financeAccounts = _config["Mcp:FinanceKingdeeAccounts"] ?? "";
        var isFinance = financeAccounts
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Contains(account, StringComparer.OrdinalIgnoreCase);

        return Task.FromResult(new KingdeeUserInfo
        {
            UserId = account,
            Name = account,
            IsFinance = isFinance
        });
    }
}
