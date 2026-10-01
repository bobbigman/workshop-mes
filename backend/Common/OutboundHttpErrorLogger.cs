using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace ahu.MicrosoftMes.Common;

/// <summary>外部 HTTP 失败统一落盘（docs/2D §4.3）。服务层业务失败与传输失败共用。</summary>
public class OutboundHttpErrorLogger
{
    private readonly ILogger<OutboundHttpErrorLogger> _logger;
    private readonly IOptionsMonitor<ErrorLoggingOptions> _options;

    public OutboundHttpErrorLogger(
        ILogger<OutboundHttpErrorLogger> logger,
        IOptionsMonitor<ErrorLoggingOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    public void Log(
        string stage,
        string method,
        string? url,
        string? requestContentType,
        string? requestBody,
        bool responseReceived,
        int? statusCode,
        string? responseContentType,
        string? responseBody,
        double durationMs,
        string? businessErrorCode,
        Exception? ex,
        bool omitAiBody = false,
        IEnumerable<KeyValuePair<string, IEnumerable<string>>>? requestHeaders = null)
    {
        var opt = _options.CurrentValue;
        var callId = Guid.NewGuid().ToString("N");
        var traceId = Activity.Current?.Id ?? Activity.Current?.TraceId.ToString() ?? "";
        var safeUrl = LogRedactor.RedactUrl(url, opt.MaxBodyBytes);
        var req = LogRedactor.RedactJsonOrForm(requestBody, opt.MaxBodyBytes, omitAiBody);
        var resp = LogRedactor.RedactJsonOrForm(responseBody, opt.MaxBodyBytes, omitAiBody);
        object headers = requestHeaders == null
            ? new { }
            : LogRedactor.RedactHeaders(requestHeaders);

        _logger.LogError(ex,
            "event=http_error stage={Stage} TraceId={TraceId} CallId={CallId} Method={Method} Url={Url} RequestContentType={RequestContentType} RequestBody={RequestBody} RequestTruncated={RequestTruncated} RequestOmitReason={RequestOmitReason} ResponseReceived={ResponseReceived} StatusCode={StatusCode} ResponseContentType={ResponseContentType} ResponseBody={ResponseBody} ResponseTruncated={ResponseTruncated} ResponseOmitReason={ResponseOmitReason} DurationMs={DurationMs} BusinessErrorCode={BusinessErrorCode} Headers={Headers} ExceptionChain={Chain}",
            stage,
            traceId,
            callId,
            method,
            safeUrl,
            requestContentType,
            req.Text,
            req.Truncated,
            req.Reason,
            responseReceived,
            statusCode,
            responseContentType,
            resp.Text,
            resp.Truncated,
            resp.Reason,
            durationMs,
            businessErrorCode,
            System.Text.Json.JsonSerializer.Serialize(headers),
            ex == null ? "" : LogRedactor.FormatExceptionChain(ex));
    }
}
