namespace ahu.MicrosoftMes.Common;

/// <summary>工厂授权档（存 sys_factory.license_tier）。</summary>
public enum LicenseTier
{
    Trial = 0,
    Enterprise = 1,
    Flagship = 2
}

/// <summary>按档控制的功能键（与前端 licenseTier.js 对齐，docs/88）。</summary>
public enum LicenseFeature
{
    PieceWage,
    PrintLabel,
    ScanReport,
    WechatDueAlert,
    ReportReview,
    MultiWorkshop,
    KingdeeMcp,
    DataScreen
}

/// <summary>功能→最低档位，单点维护。</summary>
public static class LicenseTierPolicy
{
    public static LicenseTier MinTier(LicenseFeature feature) => feature switch
    {
        LicenseFeature.PieceWage => LicenseTier.Enterprise,
        LicenseFeature.PrintLabel => LicenseTier.Enterprise,
        LicenseFeature.ScanReport => LicenseTier.Enterprise,
        LicenseFeature.WechatDueAlert => LicenseTier.Enterprise,
        LicenseFeature.ReportReview => LicenseTier.Enterprise,
        LicenseFeature.MultiWorkshop => LicenseTier.Flagship,
        LicenseFeature.KingdeeMcp => LicenseTier.Flagship,
        LicenseFeature.DataScreen => LicenseTier.Flagship,
        _ => throw ThrowHelper.General(nameof(MinTier), $"未知功能键: {feature}")
    };

    public static bool CanUse(LicenseTier tier, LicenseFeature feature) =>
        (int)tier >= (int)MinTier(feature);

    public static LicenseTier Parse(string? raw) => (raw ?? "").Trim().ToLowerInvariant() switch
    {
        "enterprise" => LicenseTier.Enterprise,
        "flagship" => LicenseTier.Flagship,
        _ => LicenseTier.Trial
    };

    public static string ToDb(LicenseTier tier) => tier switch
    {
        LicenseTier.Enterprise => "enterprise",
        LicenseTier.Flagship => "flagship",
        _ => "trial"
    };

    public static string DisplayName(LicenseTier tier) => tier switch
    {
        LicenseTier.Enterprise => "企业版",
        LicenseTier.Flagship => "旗舰版",
        _ => "入门款"
    };

    public static string FeatureDisplayName(LicenseFeature feature) => feature switch
    {
        LicenseFeature.PieceWage => "计件工资",
        LicenseFeature.PrintLabel => "打印工单/条码",
        LicenseFeature.ScanReport => "扫码报工",
        LicenseFeature.WechatDueAlert => "交期预警",
        LicenseFeature.ReportReview => "报工复核",
        LicenseFeature.MultiWorkshop => "多车间",
        LicenseFeature.KingdeeMcp => "金蝶对接",
        LicenseFeature.DataScreen => "数据大屏",
        _ => feature.ToString()
    };
}
