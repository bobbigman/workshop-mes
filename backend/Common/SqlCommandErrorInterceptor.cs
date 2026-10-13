using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace ahu.MicrosoftMes.Common;

/// <summary>EF 命令失败时快照 SQL + 参数（在释放前）。docs/2D §4.2。</summary>
public class SqlCommandErrorInterceptor : DbCommandInterceptor
{
    private readonly ILogger<SqlCommandErrorInterceptor> _logger;
    private readonly IOptionsMonitor<ErrorLoggingOptions> _options;

    public SqlCommandErrorInterceptor(
        ILogger<SqlCommandErrorInterceptor> logger,
        IOptionsMonitor<ErrorLoggingOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        LogFailure(command, eventData);
        base.CommandFailed(command, eventData);
    }

    public override Task CommandFailedAsync(
        DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        LogFailure(command, eventData);
        return base.CommandFailedAsync(command, eventData, cancellationToken);
    }

    private void LogFailure(DbCommand command, CommandErrorEventData eventData)
    {
        var maxParam = _options.CurrentValue.MaxParameterBytes;
        var snaps = SnapshotParams(command);
        var sql = command.CommandText ?? "";
        var parameters = LogRedactor.RedactSqlParameters(snaps, sql, maxParam);
        var traceId = Activity.Current?.Id ?? Activity.Current?.TraceId.ToString() ?? "";
        var commandId = eventData.CommandId.ToString();
        var durationMs = eventData.Duration.TotalMilliseconds;

        int? sqlNumber = null;
        byte? sqlState = null;
        byte? sqlClass = null;
        string? sqlProc = null;
        int? sqlLine = null;
        if (eventData.Exception is SqlException sqlEx && sqlEx.Errors.Count > 0)
        {
            var e0 = sqlEx.Errors[0];
            sqlNumber = e0.Number;
            sqlState = e0.State;
            sqlClass = e0.Class;
            sqlProc = e0.Procedure;
            sqlLine = e0.LineNumber;
        }

        var stage = string.IsNullOrWhiteSpace(sql) ? "command_not_executed_or_empty" : "command_failed";

        _logger.LogError(eventData.Exception,
            "event=sql_error stage={Stage} TraceId={TraceId} CommandId={CommandId} CommandType={CommandType} Timeout={Timeout} DurationMs={DurationMs} SqlNumber={SqlNumber} SqlState={SqlState} SqlClass={SqlClass} SqlProc={SqlProc} SqlLine={SqlLine} Sql={Sql} Parameters={Parameters} ExceptionChain={Chain}",
            stage,
            traceId,
            commandId,
            command.CommandType.ToString(),
            command.CommandTimeout,
            durationMs,
            sqlNumber,
            sqlState,
            sqlClass,
            sqlProc,
            sqlLine,
            LogRedactor.RedactText(sql, _options.CurrentValue.MaxBodyBytes),
            System.Text.Json.JsonSerializer.Serialize(parameters),
            eventData.Exception == null ? "" : LogRedactor.FormatExceptionChain(eventData.Exception));
    }

    private static List<LogRedactor.ParamSnap> SnapshotParams(DbCommand command)
    {
        var list = new List<LogRedactor.ParamSnap>(command.Parameters.Count);
        foreach (DbParameter p in command.Parameters)
        {
            list.Add(new LogRedactor.ParamSnap
            {
                Name = p.ParameterName ?? "",
                DbType = p.DbType.ToString(),
                Direction = p.Direction.ToString(),
                Size = p.Size,
                Precision = p.Precision,
                Scale = p.Scale,
                Value = p.Value is DBNull ? null : p.Value
            });
        }
        return list;
    }
}
