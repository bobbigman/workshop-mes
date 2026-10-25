using System.Data;
using System.Security.Cryptography;
using System.Text;
using ahu.MicrosoftMes.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;

namespace ahu.MicrosoftMes.Services;

public sealed record MaintenanceSqlPlan(string Sql, string SqlHash, string Scope, string Server, string Database, DateTimeOffset ExpiresAt);

/// <summary>单进程一次性计划；重启或缓存淘汰后必须重新准备，不自动重试执行。</summary>
public sealed class MaintenanceSqlPlans(IMemoryCache cache)
{
    private readonly object _gate = new();
    public string Add(MaintenanceSqlPlan plan)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        cache.Set("maintenance-sql:" + token, plan, plan.ExpiresAt);
        return token;
    }

    public MaintenanceSqlPlan Take(string token, string scope)
    {
        lock (_gate)
        {
            var key = "maintenance-sql:" + token;
            if (!cache.TryGetValue<MaintenanceSqlPlan>(key, out var plan) || plan == null
                || plan.ExpiresAt <= DateTimeOffset.UtcNow || plan.Scope != scope)
                throw ThrowHelper.BizUser("SQL确认凭据无效、过期或上下文已变化，请重新准备；执行失败时先查结果，不要直接重试");
            cache.Remove(key);
            return plan;
        }
    }
}

public sealed record MaintenanceSqlResultSet(List<string> Columns, List<List<object?>> Rows, bool Truncated);
public sealed record MaintenanceSqlResult(string Server, string Database, string SqlHash,
    int RecordsAffected, List<MaintenanceSqlResultSet> Results, bool Truncated);

public sealed class MaintenanceSqlService(ConfiguredSqlConnection connection, MaintenanceSqlPlans plans, ILogger<MaintenanceSqlService> log)
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private string ConnectionString => connection.ConnectionString;
    private string Scope(string identity) => Hash(ConnectionString + "\n" + identity);

    public async Task<object> PrepareAsync(string sql, string identity, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sql) || sql.Length > 64000)
            throw ThrowHelper.BizUser("维护SQL不能为空，最多64000字符；不支持GO等客户端指令");
        await using var conn = connection.Create();
        try
        {
            await conn.OpenAsync(ct);
            var (server, database) = await TargetAsync(conn, ct);
            var plan = new MaintenanceSqlPlan(sql, Hash(sql), Scope(identity), server, database, DateTimeOffset.UtcNow.AddMinutes(5));
            var confirmationToken = plans.Add(plan);
            log.LogWarning("SQL维护准备 identity={Identity} server={Server} database={Database} sqlHash={SqlHash}", identity, server, database, plan.SqlHash);
            return new { server, database, sql, plan.SqlHash, plan.ExpiresAt, confirmationToken,
                hint = "仅展示，尚未执行SQL。此维护权限可跨账套写库；核对完整SQL和目标后才调用execute_maintenance_sql。失败可能部分提交，不自动重试。" };
        }
        catch (Exception ex) when (ex is not BusinessException)
        {
            throw ThrowHelper.General(nameof(PrepareAsync), "连接维护SQL目标数据库失败", ex);
        }
    }

    public async Task<MaintenanceSqlResult> ExecuteAsync(string confirmationToken, string identity, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        ct = timeout.Token;
        if (string.IsNullOrWhiteSpace(confirmationToken) || confirmationToken.Length != 64)
            throw ThrowHelper.BizUser("请提供准备步骤返回的SQL确认凭据");
        var plan = plans.Take(confirmationToken, Scope(identity));
        log.LogWarning("SQL维护开始 identity={Identity} server={Server} database={Database} sqlHash={SqlHash}", identity, plan.Server, plan.Database, plan.SqlHash);
        await using var conn = connection.Create();
        try
        {
            await conn.OpenAsync(ct);
            var (server, database) = await TargetAsync(conn, ct);
            if (server != plan.Server || database != plan.Database)
                throw ThrowHelper.BizUser("实际数据库目标已变化，请重新准备维护SQL");
            await using var command = conn.CreateCommand();
            command.CommandText = plan.Sql;
            command.CommandTimeout = 30;
            var results = new List<MaintenanceSqlResultSet>();
            var truncated = false;
            var returnedChars = 0;
            int affected;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                do
                {
                    if (reader.FieldCount == 0) continue;
                    var keep = results.Count < 5;
                    var fieldCount = Math.Min(reader.FieldCount, 64);
                    var columns = Enumerable.Range(0, fieldCount).Select(reader.GetName).ToList();
                    var rows = new List<List<object?>>();
                    var setTruncated = reader.FieldCount > fieldCount || !keep;
                    truncated |= setTruncated;
                    while (await reader.ReadAsync(ct))
                    {
                        if (!keep || rows.Count >= 200 || returnedChars >= 1000000) { setTruncated = truncated = true; continue; }
                        var row = new List<object?>();
                        for (var i = 0; i < fieldCount; i++)
                        {
                            object? value;
                            if (reader.IsDBNull(i)) value = null;
                            else if (reader.GetFieldType(i) == typeof(string))
                            {
                                using var textReader = reader.GetTextReader(i);
                                var buffer = new char[4001];
                                var length = await textReader.ReadBlockAsync(buffer.AsMemory(), ct);
                                if (length > 4000) setTruncated = truncated = true;
                                value = new string(buffer, 0, Math.Min(length, 4000));
                            }
                            else if (reader.GetFieldType(i) == typeof(byte[]))
                            {
                                var length = reader.GetBytes(i, 0, null, 0, 0);
                                if (length > 2000) setTruncated = truncated = true;
                                var bytes = new byte[(int)Math.Min(length, 2000)];
                                reader.GetBytes(i, 0, bytes, 0, bytes.Length);
                                value = Convert.ToHexString(bytes);
                            }
                            else value = reader.GetValue(i);
                            if (value is string text && text.Length > 4000) { value = text[..4000]; setTruncated = truncated = true; }
                            returnedChars += value?.ToString()?.Length ?? 0;
                            row.Add(value);
                        }
                        rows.Add(row);
                    }
                    if (keep) results.Add(new(columns, rows, setTruncated));
                } while (await reader.NextResultAsync(ct));
                affected = reader.RecordsAffected;
            }
            // 遗漏COMMIT的脚本不能被报告为成功；断连时连接池也会重置事务。
            await using var check = conn.CreateCommand();
            check.CommandText = "IF @@TRANCOUNT > 0 BEGIN ROLLBACK; THROW 51500,N'维护SQL存在未提交事务，已回滚该事务',1; END;";
            check.CommandTimeout = 5;
            await check.ExecuteNonQueryAsync(ct);
            log.LogWarning("SQL维护完成 identity={Identity} server={Server} database={Database} sqlHash={SqlHash} recordsAffected={Affected}", identity, server, database, plan.SqlHash, affected);
            return new(server, database, plan.SqlHash, affected, results, truncated);
        }
        catch (Exception ex)
        {
            log.LogWarning("SQL维护失败或结果未知 identity={Identity} sqlHash={SqlHash} errorType={ErrorType}，禁止自动重试", identity, plan.SqlHash, ex.GetType().Name);
            if (ex is BusinessException) throw;
            throw ThrowHelper.Sql(plan.Sql, ex);
        }
    }

    private static async Task<(string Server, string Database)> TargetAsync(SqlConnection conn, CancellationToken ct)
    {
        await using var command = conn.CreateCommand();
        command.CommandText = "SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')),DB_NAME();";
        command.CommandTimeout = 10;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);
        if (!await reader.ReadAsync(ct)) throw ThrowHelper.BizUser("无法确认SQL维护目标");
        return (reader.GetString(0), reader.GetString(1));
    }
}
