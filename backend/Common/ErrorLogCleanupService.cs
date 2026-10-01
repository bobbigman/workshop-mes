using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace ahu.MicrosoftMes.Common;

/// <summary>
/// 按 UTC 日桶清理错误日志文件。可注入时钟便于测试。不误删是硬要求（docs/2D §6）。
/// </summary>
public class ErrorLogCleanupService : BackgroundService
{
    // mes-error-20260920.log / mes-error-20260920_001.log
    private static readonly Regex FileNameRegex = new(
        @"^mes-error-(\d{8})(?:_(\d{3}))?\.log$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IOptionsMonitor<ErrorLoggingOptions> _options;
    private readonly IHostEnvironment _env;
    private readonly ILogger<ErrorLogCleanupService> _logger;
    private readonly Func<DateTime> _utcNow;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ErrorLogCleanupService(
        IOptionsMonitor<ErrorLoggingOptions> options,
        IHostEnvironment env,
        ILogger<ErrorLogCleanupService> logger)
        : this(options, env, logger, () => DateTime.UtcNow)
    {
    }

    /// <summary>测试可注入时钟。</summary>
    public ErrorLogCleanupService(
        IOptionsMonitor<ErrorLoggingOptions> options,
        IHostEnvironment env,
        ILogger<ErrorLogCleanupService> logger,
        Func<DateTime> utcNow)
    {
        _options = options;
        _env = env;
        _logger = logger;
        _utcNow = utcNow;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动先清一次（补清停机期间）
        await RunCleanupSafeAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var minutes = Math.Max(1, _options.CurrentValue.CleanupIntervalMinutes);
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(minutes), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            await RunCleanupSafeAsync(stoppingToken);
        }
    }

    public async Task RunCleanupSafeAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct))
        {
            _logger.LogWarning("错误日志清理跳过：上一轮仍在执行");
            return;
        }
        try
        {
            CleanupOnce(_options.CurrentValue, _utcNow());
        }
        catch (Exception ex)
        {
            // 不静默；也不递归写同一失败风暴——只记一条
            _logger.LogError(ex, "错误日志清理失败");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>同步清理入口，供单元/手工验收。</summary>
    public int CleanupOnce(ErrorLoggingOptions opt, DateTime utcNow)
    {
        opt.Validate();
        var dir = opt.ResolveAbsoluteDirectory(_env.ContentRootPath);
        if (!System.IO.Directory.Exists(dir))
            return 0;

        // 拒绝目录本身为重解析点/符号链接
        if (IsReparsePoint(dir))
        {
            _logger.LogError("错误日志清理拒绝：目录为重解析点/符号链接 {Dir}", dir);
            return 0;
        }

        var today = DateOnly.FromDateTime(utcNow);
        var oldestKeep = today.AddDays(-(opt.RetentionDays - 1));
        var deleted = 0;

        foreach (var path in System.IO.Directory.EnumerateFiles(dir, "mes-error-*.log", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (!full.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(full, dir, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("跳过越界路径 {Path}", path);
                    continue;
                }

                if (IsReparsePoint(full) || IsReparsePoint(Path.GetDirectoryName(full)!))
                {
                    _logger.LogWarning("跳过重解析点/链接 {Path}", path);
                    continue;
                }

                var name = Path.GetFileName(path);
                var m = FileNameRegex.Match(name);
                if (!m.Success)
                    continue; // 无关文件不删

                if (!DateOnly.TryParseExact(m.Groups[1].Value, "yyyyMMdd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var fileDay))
                    continue;

                if (fileDay >= oldestKeep)
                    continue; // 保留窗口内（含今天与近 RetentionDays-1 天）

                // 不主动判断「当前活动文件」文件名——过期日的文件才删；当天文件不会进删除分支
                System.IO.File.Delete(full);
                deleted++;
                _logger.LogWarning("已删除过期错误日志 {File} fileDay={Day} keepFrom={Keep}",
                    name, fileDay, oldestKeep);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除日志文件失败，继续处理其它文件 Path={Path}", path);
            }
        }

        return deleted;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
                return false;
            var attr = System.IO.File.GetAttributes(path);
            return (attr & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true; // 无法判断则保守跳过
        }
    }
}
