namespace ahu.MicrosoftMes.Common;

/// <summary>错误日志文件落盘与清理配置（docs/2D）。</summary>
public class ErrorLoggingOptions
{
    public const string SectionName = "ErrorLogging";

    /// <summary>日志目录（生产须绝对路径，且按实例隔离）。</summary>
    public string Directory { get; set; } = "logs";

    /// <summary>按 UTC 日桶保留天数（今天 + 前 N-1 天）。</summary>
    public int RetentionDays { get; set; } = 14;

    public int CleanupIntervalMinutes { get; set; } = 60;

    /// <summary>单卷大小上限（字节），默认 50 MiB。</summary>
    public long FileSizeLimitBytes { get; set; } = 52_428_800;

    /// <summary>请求/响应正文单项最大 UTF-8 字节。</summary>
    public int MaxBodyBytes { get; set; } = 65_536;

    /// <summary>单个 SQL 参数值最大 UTF-8 字节。</summary>
    public int MaxParameterBytes { get; set; } = 4_096;

    /// <summary>文件名前缀，清理只匹配此前缀。</summary>
    public string FileNamePrefix { get; set; } = "mes-error-";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Directory))
            throw ThrowHelper.General(nameof(Validate), "ErrorLogging:Directory 未配置");
        if (RetentionDays < 1 || RetentionDays > 365)
            throw ThrowHelper.General(nameof(Validate), "ErrorLogging:RetentionDays 须在 1～365");
        if (CleanupIntervalMinutes < 1 || CleanupIntervalMinutes > 24 * 60)
            throw ThrowHelper.General(nameof(Validate), "ErrorLogging:CleanupIntervalMinutes 须在 1～1440");
        if (FileSizeLimitBytes < 1_048_576 || FileSizeLimitBytes > 512L * 1024 * 1024)
            throw ThrowHelper.General(nameof(Validate), "ErrorLogging:FileSizeLimitBytes 须在 1MiB～512MiB");
        if (MaxBodyBytes < 1024 || MaxBodyBytes > 2 * 1024 * 1024)
            throw ThrowHelper.General(nameof(Validate), "ErrorLogging:MaxBodyBytes 须在 1KiB～2MiB");
        if (MaxParameterBytes < 256 || MaxParameterBytes > 256 * 1024)
            throw ThrowHelper.General(nameof(Validate), "ErrorLogging:MaxParameterBytes 须在 256B～256KiB");
        if (string.IsNullOrWhiteSpace(FileNamePrefix) || FileNamePrefix.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            throw ThrowHelper.General(nameof(Validate), "ErrorLogging:FileNamePrefix 非法");
    }

    /// <summary>解析为绝对目录；相对路径相对内容根。</summary>
    public string ResolveAbsoluteDirectory(string contentRoot)
    {
        var dir = Directory.Trim();
        return Path.IsPathRooted(dir)
            ? Path.GetFullPath(dir)
            : Path.GetFullPath(Path.Combine(contentRoot, dir));
    }
}
