namespace ahu.MicrosoftMes.Common;

/// <summary>启动前探测日志目录可读写删；失败则阻止启动。</summary>
public static class ErrorLoggingBootstrap
{
    public static string PrepareDirectory(ErrorLoggingOptions opt, string contentRoot, string instanceCode)
    {
        opt.Validate();
        var dir = opt.ResolveAbsoluteDirectory(contentRoot);

        // 开发可用相对路径；生产应配绝对路径（文档约束）
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            throw ThrowHelper.General(nameof(PrepareDirectory),
                $"无法创建错误日志目录 {dir}（实例 {instanceCode}）", ex);
        }

        var probe = Path.Combine(dir, $".mes-log-probe-{instanceCode}-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, "probe");
            var read = File.ReadAllText(probe);
            if (read != "probe")
                throw ThrowHelper.General(nameof(PrepareDirectory), $"错误日志目录读回不一致: {dir}");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is not BusinessException)
        {
            TryDeleteQuiet(probe);
            throw ThrowHelper.General(nameof(PrepareDirectory),
                $"错误日志目录不可写/不可删: {dir}。请检查服务账号权限与磁盘空间。", ex);
        }

        return dir;
    }

    private static void TryDeleteQuiet(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* 探测清理失败不掩盖主错 */ }
    }

    /// <summary>Serilog 文件路径模板：mes-error-.log → mes-error-yyyyMMdd[_NNN].log</summary>
    public static string BuildFilePathTemplate(string absoluteDirectory) =>
        Path.Combine(absoluteDirectory, "mes-error-.log");
}
