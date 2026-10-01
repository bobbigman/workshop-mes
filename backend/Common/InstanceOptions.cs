namespace ahu.MicrosoftMes.Common;

/// <summary>
/// 客户独立部署实例配置（docs/23）。每客户一份程序进程，连接固定库；不动态选库。
/// </summary>
public class InstanceOptions
{
    public const string SectionName = "Instance";

    /// <summary>运维标识，仅字母数字与连字符；写入 Jwt Issuer/Audience 校验。</summary>
    public string Code { get; set; } = "";

    /// <summary>登录页与页头展示的客户名称。</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>本实例唯一服务的工厂代码。</summary>
    public string FactoryCode { get; set; } = "";

    /// <summary>对外链接基址；生成二维码/外链时用，不信任请求 Host。</summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>为 true 时启动注入演示种子（本地开发）；客户试用必须 false。</summary>
    public bool SeedDemoData { get; set; }

    /// <summary>为 true 时注册 /mcp 与 mcp-auth；客户试用默认 false。</summary>
    public bool EnableMcp { get; set; }

    /// <summary>为 true 时开放 Swagger；公网试用建议 false。</summary>
    public bool EnableSwagger { get; set; }

    /// <summary>为 true 时允许固定验证码手机号登录；客户试用必须 false。</summary>
    public bool AllowPhoneLogin { get; set; }

    /// <summary>
    /// 为 true 时启动创建工厂与管理员（若不存在）；不重置已有密码。
    /// 首次开通用完后必须改回 false。
    /// </summary>
    public bool BootstrapOnStartup { get; set; }

    public string BootstrapAdminAccount { get; set; } = "admin";
    public string BootstrapAdminName { get; set; } = "管理员";
    public string BootstrapAdminPassword { get; set; } = "";
}

/// <summary>运行期缓存：本实例工厂 Id（启动后解析）。</summary>
public class InstanceRuntime
{
    public long? FactoryId { get; set; }
}

public static class InstanceConfigValidator
{
    public static void Validate(InstanceOptions instance, JwtOptions jwt)
    {
        if (string.IsNullOrWhiteSpace(instance.Code))
            throw ThrowHelper.General(nameof(Validate), "Instance:Code 未配置");
        if (!System.Text.RegularExpressions.Regex.IsMatch(instance.Code, @"^[A-Za-z0-9\-]+$"))
            throw ThrowHelper.General(nameof(Validate), "Instance:Code 仅允许字母、数字、连字符");
        if (string.IsNullOrWhiteSpace(instance.DisplayName))
            throw ThrowHelper.General(nameof(Validate), "Instance:DisplayName 未配置");
        if (string.IsNullOrWhiteSpace(instance.FactoryCode))
            throw ThrowHelper.General(nameof(Validate), "Instance:FactoryCode 未配置");

        if (string.IsNullOrWhiteSpace(jwt.Secret) || jwt.Secret.Length < 32)
            throw ThrowHelper.General(nameof(Validate), "Jwt:Secret 未配置或长度不足32位");

        var expectedIssuer = $"mes-{instance.Code}";
        var expectedAudience = $"mes-{instance.Code}-app";
        if (!string.Equals(jwt.Issuer, expectedIssuer, StringComparison.Ordinal))
            throw ThrowHelper.General(nameof(Validate),
                $"Jwt:Issuer 必须为 {expectedIssuer}（当前: {jwt.Issuer}）");
        if (!string.Equals(jwt.Audience, expectedAudience, StringComparison.Ordinal))
            throw ThrowHelper.General(nameof(Validate),
                $"Jwt:Audience 必须为 {expectedAudience}（当前: {jwt.Audience}）");

        if (instance.SeedDemoData &&
            !string.Equals(instance.FactoryCode, "F001", StringComparison.OrdinalIgnoreCase))
            throw ThrowHelper.General(nameof(Validate),
                "Instance:SeedDemoData=true 时 FactoryCode 必须为 F001（演示种子固定）");

        if (instance.BootstrapOnStartup && string.IsNullOrWhiteSpace(instance.BootstrapAdminPassword))
            throw ThrowHelper.General(nameof(Validate),
                "Instance:BootstrapOnStartup=true 时必须配置 BootstrapAdminPassword");
    }
}
