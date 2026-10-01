namespace ahu.MicrosoftMes.Common;

/// <summary>轻量云授权（docs/89）。增量包勿覆盖服务器本配置。</summary>
public class LicenseCloudOptions
{
    public const string SectionName = "LicenseCloud";

    /// <summary>默认 false：开发/未开通客户只读本地 license_tier。</summary>
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "";

    /// <summary>断网宽限天数，默认 7。</summary>
    public int GraceDays { get; set; } = 7;

    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>可选；非空时请求头带 X-License-Key。</summary>
    public string SharedSecret { get; set; } = "";
}
