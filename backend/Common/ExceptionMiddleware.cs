using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ahu.MicrosoftMes.Common;

/// <summary>
/// 全局异常中间件：不吞异常；服务器日志保留完整链 + X-Trace-Id；
/// 技术故障的 msg 回传 SQL/报文等上下文到 PC/H5，便于现场排查（密钥仍脱敏）。
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation("客户端取消请求 TraceId={TraceId} Path={Path}",
                TraceIdMiddleware.Get(context), context.Request.Path.Value);
            // 不写 JSON 体；连接已取消
        }
        catch (BusinessException ex)
        {
            var traceId = TraceIdMiddleware.Get(context);
            if (LogRedactor.LooksTechnical(ex.Message))
            {
                LogTechnical(context, ex, traceId, "业务异常含技术上下文");
                await WriteSafeAsync(context, HttpStatusCode.OK,
                    ApiResult<object?>.FailMsg(ClientTechMsg(ex), 500));
            }
            else
            {
                _logger.LogWarning(ex,
                    "业务异常 TraceId={TraceId} UserId={UserId} FactoryId={FactoryId} Path={Path} Msg={Msg}",
                    traceId, ReadClaim(context, JwtHelper.ClaimUserId), ReadClaim(context, JwtHelper.ClaimFactoryId),
                    context.Request.Path.Value, ex.Message);
                await WriteSafeAsync(context, HttpStatusCode.OK,
                    ApiResult<object?>.FailMsg(ex.Message, ex.Code));
            }
        }
        catch (Exception ex)
        {
            var traceId = TraceIdMiddleware.Get(context);
            LogTechnical(context, ex, traceId, "未处理技术异常");
            await WriteSafeAsync(context, HttpStatusCode.OK,
                ApiResult<object?>.FailMsg(ClientTechMsg(ex), 500));
        }
    }

    private void LogTechnical(HttpContext context, Exception ex, string traceId, string op)
    {
        _logger.LogError(ex,
            "event=request_error op={Op} TraceId={TraceId} UserId={UserId} FactoryId={FactoryId} Method={Method} Path={Path} ExceptionChain={Chain}",
            op,
            traceId,
            ReadClaim(context, JwtHelper.ClaimUserId),
            ReadClaim(context, JwtHelper.ClaimFactoryId),
            context.Request.Method,
            LogRedactor.RedactUrl(context.Request.Path + context.Request.QueryString.Value),
            LogRedactor.FormatExceptionChain(ex));
    }

    /// <summary>前台可见：异常链 Message（含 SQL/报文），不含堆栈；密码/Token 脱敏。</summary>
    private static string ClientTechMsg(Exception ex)
    {
        var sb = new StringBuilder();
        var cur = ex;
        var depth = 0;
        while (cur != null && depth < 8)
        {
            var m = cur.Message?.Trim();
            if (!string.IsNullOrEmpty(m))
            {
                if (sb.Length > 0) sb.Append(" ← ");
                sb.Append(m);
            }
            cur = cur.InnerException;
            depth++;
        }

        var text = sb.Length > 0 ? sb.ToString() : "操作失败";
        return LogRedactor.RedactText(text, 8192);
    }

    private static string? ReadClaim(HttpContext ctx, string type)
    {
        if (ctx.User?.Identity?.IsAuthenticated != true) return null;
        return ctx.User.FindFirst(type)?.Value;
    }

    private static async Task WriteSafeAsync(HttpContext context, HttpStatusCode status, object body)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = (int)status;
        if (!context.Response.Headers.ContainsKey(TraceIdMiddleware.HeaderName))
            context.Response.Headers[TraceIdMiddleware.HeaderName] = TraceIdMiddleware.Get(context);

        await context.Response.WriteAsync(JsonSerializer.Serialize(body, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All)
        }));
    }
}
