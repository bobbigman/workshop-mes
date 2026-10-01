using System.Diagnostics;

namespace ahu.MicrosoftMes.Common;

/// <summary>
/// 传输层失败 / 非 2xx 自动记录。HTTP 200 业务失败仍须服务层调用 <see cref="OutboundHttpErrorLogger"/>。
/// </summary>
public class OutboundHttpLoggingHandler : DelegatingHandler
{
    private readonly OutboundHttpErrorLogger _errorLogger;

    public OutboundHttpLoggingHandler(OutboundHttpErrorLogger errorLogger)
    {
        _errorLogger = errorLogger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? reqBody = null;
        if (request.Content != null)
        {
            // 缓冲以便日志与后续业务再读；限制由 Redactor 截断
            reqBody = await request.Content.ReadAsStringAsync(cancellationToken);
            var mediaType = request.Content.Headers.ContentType?.MediaType ?? "application/json";
            request.Content = new StringContent(reqBody, System.Text.Encoding.UTF8, mediaType);
        }

        var headers = request.Headers
            .Select(h => new KeyValuePair<string, IEnumerable<string>>(h.Key, h.Value))
            .Concat(request.Content?.Headers.Select(h =>
                new KeyValuePair<string, IEnumerable<string>>(h.Key, h.Value))
                ?? Array.Empty<KeyValuePair<string, IEnumerable<string>>>());

        var omitAi = request.RequestUri?.Host.Contains("deepseek", StringComparison.OrdinalIgnoreCase) == true
                     || request.RequestUri?.AbsolutePath.Contains("chat/completions", StringComparison.OrdinalIgnoreCase) == true;

        HttpResponseMessage? resp = null;
        try
        {
            resp = await base.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            sw.Stop();
            _errorLogger.Log(
                stage: "timeout_or_canceled",
                method: request.Method.Method,
                url: request.RequestUri?.ToString(),
                requestContentType: request.Content?.Headers.ContentType?.ToString(),
                requestBody: reqBody,
                responseReceived: false,
                statusCode: null,
                responseContentType: null,
                responseBody: null,
                durationMs: sw.Elapsed.TotalMilliseconds,
                businessErrorCode: null,
                ex: ex,
                omitAiBody: omitAi,
                requestHeaders: headers);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _errorLogger.Log(
                stage: "send_failed",
                method: request.Method.Method,
                url: request.RequestUri?.ToString(),
                requestContentType: request.Content?.Headers.ContentType?.ToString(),
                requestBody: reqBody,
                responseReceived: false,
                statusCode: null,
                responseContentType: null,
                responseBody: null,
                durationMs: sw.Elapsed.TotalMilliseconds,
                businessErrorCode: null,
                ex: ex,
                omitAiBody: omitAi,
                requestHeaders: headers);
            throw;
        }

        if (!resp.IsSuccessStatusCode)
        {
            string? respText = null;
            try
            {
                respText = await resp.Content.ReadAsStringAsync(cancellationToken);
                var media = resp.Content.Headers.ContentType?.MediaType ?? "application/json";
                resp.Content = new StringContent(respText, System.Text.Encoding.UTF8, media);
            }
            catch (Exception readEx)
            {
                sw.Stop();
                _errorLogger.Log(
                    stage: "response_read_failed",
                    method: request.Method.Method,
                    url: request.RequestUri?.ToString(),
                    requestContentType: request.Content?.Headers.ContentType?.ToString(),
                    requestBody: reqBody,
                    responseReceived: true,
                    statusCode: (int)resp.StatusCode,
                    responseContentType: resp.Content.Headers.ContentType?.ToString(),
                    responseBody: null,
                    durationMs: sw.Elapsed.TotalMilliseconds,
                    businessErrorCode: null,
                    ex: readEx,
                    omitAiBody: omitAi,
                    requestHeaders: headers);
                return resp;
            }

            sw.Stop();
            _errorLogger.Log(
                stage: "non_2xx",
                method: request.Method.Method,
                url: request.RequestUri?.ToString(),
                requestContentType: request.Content?.Headers.ContentType?.ToString(),
                requestBody: reqBody,
                responseReceived: true,
                statusCode: (int)resp.StatusCode,
                responseContentType: resp.Content.Headers.ContentType?.ToString(),
                responseBody: respText,
                durationMs: sw.Elapsed.TotalMilliseconds,
                businessErrorCode: null,
                ex: null,
                omitAiBody: omitAi,
                requestHeaders: headers);
        }

        return resp;
    }
}
