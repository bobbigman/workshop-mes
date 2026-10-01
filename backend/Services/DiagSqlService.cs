using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;

namespace ahu.MicrosoftMes.Services;

public class DiagSqlRequest
{
    public string Sql { get; set; } = "";
}

public class DiagSqlResultDto
{
    public List<string> Columns { get; set; } = new();
    public List<List<object?>> Rows { get; set; } = new();
    public int RowCount { get; set; }
    public bool Truncated { get; set; }
    public long ElapsedMs { get; set; }
}

public interface IDiagSqlService
{
    Task<ApiResult<DiagSqlResultDto>> QueryAsync(string sql, long userId, CancellationToken ct);
}

/// <summary>管理员只读诊断 SQL（docs/59）。不挂 MCP。</summary>
public class DiagSqlService : IDiagSqlService
{
    public const int MaxSqlChars = 8000;
    public const int MaxRows = 200;
    public const int CommandTimeoutSeconds = 15;

    private static readonly Regex Forbidden = new(
        @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE|GRANT|REVOKE|BACKUP|RESTORE|SHUTDOWN|DBCC|OPENROWSET|OPENDATASOURCE|XP_)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly AppDbContext _db;
    private readonly ILogger<DiagSqlService> _log;

    public DiagSqlService(AppDbContext db, ILogger<DiagSqlService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<ApiResult<DiagSqlResultDto>> QueryAsync(string sql, long userId, CancellationToken ct)
    {
        var raw = sql ?? "";
        if (string.IsNullOrWhiteSpace(raw))
            throw ThrowHelper.BizUser("请填写要查询的 SQL");
        if (raw.Length > MaxSqlChars)
            throw ThrowHelper.BizUser($"SQL 过长，最多 {MaxSqlChars} 字符");

        var normalized = NormalizeForCheck(raw);
        EnsureSelectOnly(normalized);

        var cs = _db.Database.GetDbConnection().ConnectionString
            ?? throw ThrowHelper.General(nameof(QueryAsync), "数据库连接串为空");

        _log.LogInformation("DiagSql userId={UserId} sqlChars={Len}", userId, raw.Length);

        var sw = Stopwatch.StartNew();
        try
        {
            await using var conn = new SqlConnection(cs);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(raw, conn)
            {
                CommandType = CommandType.Text,
                CommandTimeout = CommandTimeoutSeconds
            };

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var columns = new List<string>();
            for (var i = 0; i < reader.FieldCount; i++)
                columns.Add(reader.GetName(i));

            var rows = new List<List<object?>>();
            var truncated = false;
            while (await reader.ReadAsync(ct))
            {
                if (rows.Count >= MaxRows)
                {
                    truncated = true;
                    break;
                }

                var row = new List<object?>(reader.FieldCount);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    if (reader.IsDBNull(i))
                    {
                        row.Add(null);
                        continue;
                    }

                    var v = reader.GetValue(i);
                    row.Add(v is DateTime dt ? dt.ToString("O") : v);
                }

                rows.Add(row);
            }

            sw.Stop();
            return ApiResult<DiagSqlResultDto>.Ok(new DiagSqlResultDto
            {
                Columns = columns,
                Rows = rows,
                RowCount = rows.Count,
                Truncated = truncated,
                ElapsedMs = sw.ElapsedMilliseconds
            });
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw ThrowHelper.Sql(raw, ex);
        }
    }

    /// <summary>去掉块注释/行注释与多余空白，便于关键词检查。</summary>
    public static string NormalizeForCheck(string sql)
    {
        var noBlock = Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        var noLine = Regex.Replace(noBlock, @"--.*?$", " ", RegexOptions.Multiline);
        return Regex.Replace(noLine, @"\s+", " ").Trim();
    }

    public static void EnsureSelectOnly(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
            throw ThrowHelper.BizUser("请填写要查询的 SQL");

        // 禁止多语句：去掉字符串字面量后再看分号后是否还有内容
        var withoutStrings = Regex.Replace(normalized, @"('([^']|'')*')", "''");
        var semi = withoutStrings.IndexOf(';');
        if (semi >= 0 && semi < withoutStrings.Length - 1
            && !string.IsNullOrWhiteSpace(withoutStrings[(semi + 1)..]))
            throw ThrowHelper.BizUser("只允许单条 SELECT，不能一次执行多条语句");

        var head = withoutStrings.TrimStart().TrimEnd(';', ' ', '\t');
        if (!(head.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
              || head.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)))
            throw ThrowHelper.BizUser("只允许 SELECT 查询（可用 WITH…SELECT）");

        if (Forbidden.IsMatch(head))
            throw ThrowHelper.BizUser("禁止写库或管理类语句，仅支持只读 SELECT");

        // SELECT INTO 会建表/写数据
        if (Regex.IsMatch(head, @"\bINTO\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw ThrowHelper.BizUser("禁止 SELECT INTO，请改用普通 SELECT");
    }
}
