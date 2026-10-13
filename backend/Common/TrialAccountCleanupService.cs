using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Common;

/// <summary>
/// 体验账套到期清场：按 factory 隔离、依赖倒序删业务资料 + 附件目录，写 trial_cleaned_at_utc。
/// 结构照抄 ErrorLogCleanupService（门闩、时钟注入、同步入口）。docs/90。
/// </summary>
public class TrialAccountCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<TrialCleanupOptions> _options;
    private readonly IOptionsMonitor<DeploymentOptions> _deployment;
    private readonly IConfiguration _config;
    private readonly ILogger<TrialAccountCleanupService> _logger;
    private readonly Func<DateTime> _utcNow;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TrialAccountCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<TrialCleanupOptions> options,
        IOptionsMonitor<DeploymentOptions> deployment,
        IConfiguration config,
        ILogger<TrialAccountCleanupService> logger)
        : this(scopeFactory, options, deployment, config, logger, () => DateTime.UtcNow)
    {
    }

    /// <summary>测试可注入时钟。</summary>
    public TrialAccountCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<TrialCleanupOptions> options,
        IOptionsMonitor<DeploymentOptions> deployment,
        IConfiguration config,
        ILogger<TrialAccountCleanupService> logger,
        Func<DateTime> utcNow)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _deployment = deployment;
        _config = config;
        _logger = logger;
        _utcNow = utcNow;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunCleanupSafeAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var minutes = Math.Max(1, _options.CurrentValue.IntervalMinutes);
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
            _logger.LogWarning("体验账套清理跳过：上一轮仍在执行");
            return;
        }
        try
        {
            await CleanupOnceAsync(_options.CurrentValue, _deployment.CurrentValue, _utcNow(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "体验账套清理失败");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>同步/验收入口：清理到期体验账套，返回成功清场的工厂数。</summary>
    public Task<int> CleanupOnceAsync(
        TrialCleanupOptions opt,
        DeploymentOptions deployment,
        DateTime utcNow,
        CancellationToken ct = default) =>
        CleanupOnceCoreAsync(opt, deployment, utcNow, ct);

    private async Task<int> CleanupOnceCoreAsync(
        TrialCleanupOptions opt,
        DeploymentOptions deployment,
        DateTime utcNow,
        CancellationToken ct)
    {
        opt.Validate();
        var cutoff = utcNow.AddMinutes(-Math.Max(0, opt.GraceMinutes));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var candidates = await db.Factories
            .Where(f => f.LicenseTier == "trial"
                && f.TrialExpiresAtUtc != null
                && f.TrialExpiresAtUtc < cutoff
                && f.TrialCleanedAtUtc == null)
            .OrderBy(f => f.Id)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return 0;

        var codes = string.Join(",", candidates.Select(f => f.FactoryCode));
        if (deployment.Private || !opt.Enabled)
        {
            _logger.LogWarning(
                "发现 {Count} 个到期体验账套未清理（Enabled={Enabled} Private={Private}）：{Codes}",
                candidates.Count, opt.Enabled, deployment.Private, codes);
            return 0;
        }

        var cleaned = 0;
        foreach (var factory in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await CleanOneFactoryAsync(db, factory, utcNow, ct))
                    cleaned++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "体验账套清场失败 FactoryCode={FactoryCode} FactoryId={FactoryId}，已回滚该厂，继续下一厂",
                    factory.FactoryCode, factory.Id);
            }
        }

        return cleaned;
    }

    private async Task<bool> CleanOneFactoryAsync(
        AppDbContext db, SysFactory factory, DateTime utcNow, CancellationToken ct)
    {
        var fid = factory.Id;
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            // 依赖倒序：业务→基础；产品先于工艺路线（对齐 AGENTS 删除倒序）
            await DelAsync(db, counts, "salary_statement_item",
                @"DELETE FROM salary_statement_item WHERE statement_id IN
                  (SELECT id FROM salary_statement WHERE factory_id = @fid)", fid, ct);
            await DelAsync(db, counts, "salary_statement",
                "DELETE FROM salary_statement WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "prod_report_change_log",
                "DELETE FROM prod_report_change_log WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "prod_report",
                "DELETE FROM prod_report WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "prod_abnormal",
                "DELETE FROM prod_abnormal WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "prod_work_order_operation_assignee",
                @"DELETE FROM prod_work_order_operation_assignee WHERE work_order_operation_id IN
                  (SELECT t.id FROM prod_work_order_operation t
                   INNER JOIN prod_work_order o ON o.id = t.work_order_id WHERE o.factory_id = @fid)", fid, ct);
            await DelAsync(db, counts, "prod_work_order_operation",
                @"DELETE FROM prod_work_order_operation WHERE work_order_id IN
                  (SELECT id FROM prod_work_order WHERE factory_id = @fid)", fid, ct);
            await DelAsync(db, counts, "prod_work_order",
                "DELETE FROM prod_work_order WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "base_knowledge_file",
                "DELETE FROM base_knowledge_file WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "base_price_rule",
                "DELETE FROM base_price_rule WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "sys_custom_field_value",
                @"DELETE FROM sys_custom_field_value WHERE field_id IN
                  (SELECT id FROM sys_custom_field WHERE factory_id = @fid)", fid, ct);
            await DelAsync(db, counts, "sys_custom_field_option",
                @"DELETE FROM sys_custom_field_option WHERE field_id IN
                  (SELECT id FROM sys_custom_field WHERE factory_id = @fid)", fid, ct);
            await DelAsync(db, counts, "sys_custom_field",
                "DELETE FROM sys_custom_field WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "sys_wechat_send_attempt", "DELETE FROM sys_wechat_send_attempt WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "sys_wechat_alert_delivery", "DELETE FROM sys_wechat_alert_delivery WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "sys_wechat_alert_rule", "DELETE FROM sys_wechat_alert_rule WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "sys_wechat_alert_log",
                "DELETE FROM sys_wechat_alert_log WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "sys_wechat_alert_setting",
                "DELETE FROM sys_wechat_alert_setting WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "sys_print_setting",
                "DELETE FROM sys_print_setting WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "sys_worker_view_setting",
                "DELETE FROM sys_worker_view_setting WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "base_product",
                "DELETE FROM base_product WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "base_routing_step",
                @"DELETE FROM base_routing_step WHERE routing_id IN
                  (SELECT id FROM base_routing WHERE factory_id = @fid)", fid, ct);
            await DelAsync(db, counts, "base_routing",
                "DELETE FROM base_routing WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "base_operation_defect",
                @"DELETE FROM base_operation_defect WHERE operation_id IN
                  (SELECT id FROM base_operation WHERE factory_id = @fid)", fid, ct);
            await DelAsync(db, counts, "base_operation_department",
                @"DELETE FROM base_operation_department WHERE operation_id IN
                  (SELECT id FROM base_operation WHERE factory_id = @fid)", fid, ct);
            await DelAsync(db, counts, "base_operation",
                "DELETE FROM base_operation WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "base_defect_item",
                "DELETE FROM base_defect_item WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "sys_department_user",
                @"DELETE FROM sys_department_user WHERE department_id IN
                  (SELECT id FROM sys_department WHERE factory_id = @fid)", fid, ct);
            await DelAsync(db, counts, "sys_department",
                "DELETE FROM sys_department WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "base_unit",
                "DELETE FROM base_unit WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "login_setting",
                "DELETE FROM login_setting WHERE factory_id = @fid", fid, ct);
            await DelAsync(db, counts, "sys_mcp_auth",
                "DELETE FROM sys_mcp_auth WHERE factory_id = @fid", fid, ct);

            // 重新附着并写清理标记（候选列表可能已 Detach）
            var tracked = await db.Factories.FirstAsync(f => f.Id == fid, ct);
            tracked.TrialCleanedAtUtc = utcNow;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            var summary = string.Join(";", counts.Select(kv => $"{kv.Key}={kv.Value}"));
            _logger.LogWarning(
                "体验账套已清场 FactoryCode={FactoryCode} FactoryId={FactoryId} Rows={Rows}",
                factory.FactoryCode, fid, summary);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            throw ThrowHelper.General(nameof(CleanOneFactoryAsync),
                $"清场事务失败 FactoryCode={factory.FactoryCode}", ex);
        }

        // 磁盘删除放在提交成功之后
        var deletedFiles = TryDeleteFactoryDisk(fid, factory.FactoryCode);
        _logger.LogWarning(
            "体验账套附件目录处理完毕 FactoryCode={FactoryCode} DeletedEntries={Deleted}",
            factory.FactoryCode, deletedFiles);

        return true;
    }

    private static async Task DelAsync(
        AppDbContext db,
        Dictionary<string, int> counts,
        string table,
        string sql,
        long factoryId,
        CancellationToken ct)
    {
        var n = await db.Database.ExecuteSqlRawAsync(sql, new object[] { new SqlParameter("@fid", factoryId) }, ct);
        counts[table] = n;
    }

    private int TryDeleteFactoryDisk(long factoryId, string factoryCode)
    {
        var root = (_config["KnowledgeBase:RootPath"] ?? "").Trim();
        if (string.IsNullOrWhiteSpace(root))
        {
            _logger.LogWarning("体验清场跳过磁盘：未配置 KnowledgeBase:RootPath FactoryCode={Code}", factoryCode);
            return 0;
        }

        string rootFull;
        try
        {
            rootFull = Path.GetFullPath(root);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "体验清场拒绝：RootPath 无法解析 Path={Path}", root);
            return 0;
        }

        if (IsReparsePoint(rootFull))
        {
            _logger.LogError("体验清场拒绝：RootPath 为重解析点/符号链接 {Dir}", rootFull);
            return 0;
        }

        var target = Path.GetFullPath(Path.Combine(rootFull, factoryId.ToString()));
        var rootPrefix = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
        if (!target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("体验清场拒绝：路径越界 Target={Target} Root={Root}", target, rootFull);
            return 0;
        }

        if (!Directory.Exists(target))
            return 0;

        if (IsReparsePoint(target))
        {
            _logger.LogWarning("体验清场跳过：工厂目录为重解析点 {Dir}", target);
            return 0;
        }

        try
        {
            var entries = Directory.GetFileSystemEntries(target, "*", SearchOption.AllDirectories).Length;
            Directory.Delete(target, recursive: true);
            return entries;
        }
        catch (Exception ex)
        {
            // 库已清、账号将禁用；磁盘失败只记日志，不回滚
            _logger.LogError(ex,
                "体验清场磁盘删除失败（库已提交）FactoryCode={Code} Dir={Dir}",
                factoryCode, target);
            return 0;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return false;
            var attr = File.GetAttributes(path);
            return (attr & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true;
        }
    }
}
