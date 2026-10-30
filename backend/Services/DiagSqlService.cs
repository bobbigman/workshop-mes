using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using ahu.MicrosoftMes.Common;

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

    private readonly ConfiguredSqlConnection _connection;
    private readonly ILogger<DiagSqlService> _log;

    public DiagSqlService(ConfiguredSqlConnection connection, ILogger<DiagSqlService> log)
    {
        _connection = connection;
        _log = log;
    }

    public async Task<ApiResult<DiagSqlResultDto>> QueryAsync(string sql, long userId, CancellationToken ct)
    {
        var raw = sql ?? "";
        if (string.IsNullOrWhiteSpace(raw))
            throw ThrowHelper.BizUser("诊断查询需要先填写要执行的查询语句。请在查询框中输入 SELECT 语句后再点击执行。");
        if (raw.Length > MaxSqlChars)
            throw ThrowHelper.BizUser($"查询语句太长，最多 {MaxSqlChars} 个字符。请精简查询条件或减少查询字段后重试。");

        var normalized = NormalizeForCheck(raw);
        EnsureSelectOnly(normalized);

        _log.LogInformation("DiagSql userId={UserId} sqlChars={Len}", userId, raw.Length);

        var sw = Stopwatch.StartNew();
        try
        {
            await using var conn = _connection.Create();
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
            throw ThrowHelper.BizUser("诊断查询每次只能执行一条 SELECT 语句，不能一次执行多条。请拆成多次查询分别执行。");

        var head = withoutStrings.TrimStart().TrimEnd(';', ' ', '\t');
        if (!(head.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
              || head.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)))
            throw ThrowHelper.BizUser("诊断查询只允许 SELECT 查询（包括 WITH…SELECT 写法）。INSERT、UPDATE、DELETE 等写操作请使用维护 SQL 功能。");

        if (Forbidden.IsMatch(head))
            throw ThrowHelper.BizUser("诊断查询不允许执行写入或管理类语句，只支持只读 SELECT 查询。如需修改数据，请使用维护 SQL 功能。");

        // SELECT INTO 会建表/写数据
        if (Regex.IsMatch(head, @"\bINTO\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw ThrowHelper.BizUser("诊断查询不支持 SELECT INTO 写法，请改用普通 SELECT；如需建表请使用维护 SQL 功能。");
    }
}
