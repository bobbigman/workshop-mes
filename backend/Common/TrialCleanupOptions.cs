namespace ahu.MicrosoftMes.Common;

/// <summary>体验账套到期自动清理配置（docs/90）。</summary>
public class TrialCleanupOptions
{
    public const string SectionName = "TrialCleanup";

    /// <summary>清理总开关；false=跳过删除，仍记未清理日志。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>后台扫描间隔（分钟）。</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>到期后再留的宽限（分钟）；0=到点即清。</summary>
    public int GraceMinutes { get; set; }

    /// <summary>体验期天数；新建 SaaS 账套时 trial_expires_at_utc = 创建 + 此值。</summary>
    public int DurationDays { get; set; } = 7;

    public void Validate()
    {
        if (IntervalMinutes < 1 || IntervalMinutes > 24 * 60)
            throw ThrowHelper.General(nameof(Validate), "TrialCleanup:IntervalMinutes 须在 1～1440");
        if (GraceMinutes < 0 || GraceMinutes > 30 * 24 * 60)
            throw ThrowHelper.General(nameof(Validate), "TrialCleanup:GraceMinutes 须在 0～43200");
        if (DurationDays < 1 || DurationDays > 365)
            throw ThrowHelper.General(nameof(Validate), "TrialCleanup:DurationDays 须在 1～365");
    }
}
