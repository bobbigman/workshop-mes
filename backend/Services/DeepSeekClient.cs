using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ahu.MicrosoftMes.Common;
using Microsoft.Extensions.Options;

namespace ahu.MicrosoftMes.Services;

public class DeepSeekToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public DeepSeekFunctionCall Function { get; set; } = new();
}

public class DeepSeekFunctionCall
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "{}";
}

public class DeepSeekChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<DeepSeekToolCall>? ToolCalls { get; set; }
}

public class DeepSeekToolDef
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public DeepSeekFunctionDef Function { get; set; } = new();
}

public class DeepSeekFunctionDef
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("parameters")]
    public object Parameters { get; set; } = new { type = "object", properties = new { } };
}

public class DeepSeekUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int? PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int? CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int? TotalTokens { get; set; }
}

public class DeepSeekChatResult
{
    public string? Content { get; set; }
    public List<DeepSeekToolCall> ToolCalls { get; set; } = new();
    public DeepSeekUsage? Usage { get; set; }
    public string RawSafeSummary { get; set; } = "";
}

public interface IDeepSeekClient
{
    Task<DeepSeekChatResult> ChatAsync(
        IReadOnlyList<DeepSeekChatMessage> messages,
        IReadOnlyList<DeepSeekToolDef>? tools,
        CancellationToken ct);

    Task<DeepSeekChatResult> ConnectionTestAsync(CancellationToken ct);
}

public class DeepSeekClient : IDeepSeekClient
{
    public const string HttpClientName = "deepseek";
    private static readonly JsonSerializerOptions JsonOpt = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly AiOptions _opt;
    private readonly ILogger<DeepSeekClient> _logger;
    private readonly OutboundHttpErrorLogger _httpErrorLogger;

    public DeepSeekClient(
        IHttpClientFactory httpFactory,
        IOptions<AiOptions> opt,
        ILogger<DeepSeekClient> logger,
        OutboundHttpErrorLogger httpErrorLogger)
    {
        _httpFactory = httpFactory;
        _opt = opt.Value;
        _logger = logger;
        _httpErrorLogger = httpErrorLogger;
    }

    public Task<DeepSeekChatResult> ConnectionTestAsync(CancellationToken ct)
    {
        var messages = new List<DeepSeekChatMessage>
        {
            new() { Role = "user", Content = "ping" }
        };
        return ChatInternalAsync(messages, tools: null, maxTokens: 8, ct);
    }

    public Task<DeepSeekChatResult> ChatAsync(
        IReadOnlyList<DeepSeekChatMessage> messages,
        IReadOnlyList<DeepSeekToolDef>? tools,
        CancellationToken ct)
        => ChatInternalAsync(messages, tools, maxTokens: null, ct);

    private async Task<DeepSeekChatResult> ChatInternalAsync(
        IReadOnlyList<DeepSeekChatMessage> messages,
        IReadOnlyList<DeepSeekToolDef>? tools,
        int? maxTokens,
        CancellationToken ct)
    {
        if (!_opt.IsConfigured)
            throw ThrowHelper.Biz(nameof(ChatInternalAsync), "未配置 Ai:ApiKey，请联系部署人员配置环境变量 Ai__ApiKey");
        if (!_opt.HasModel)
            throw ThrowHelper.Biz(nameof(ChatInternalAsync), "未配置 Ai:Model，请在服务器填写并做连接测试");

        var baseUrl = (_opt.BaseUrl ?? "").Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw ThrowHelper.Biz(nameof(ChatInternalAsync), "Ai:BaseUrl 未配置");

        var url = baseUrl + "/chat/completions";
        var bodyObj = new Dictionary<string, object?>
        {
            ["model"] = _opt.Model.Trim(),
            ["messages"] = messages,
            ["stream"] = false
        };
        if (tools is { Count: > 0 })
            bodyObj["tools"] = tools;
        if (maxTokens != null)
            bodyObj["max_tokens"] = maxTokens.Value;

        var json = JsonSerializer.Serialize(bodyObj, JsonOpt);
        // 日志只记结构摘要；消息正文不落盘（AI 隐私）
        var safeReq = $"model={_opt.Model}; msgs={messages.Count}; tools={(tools?.Count ?? 0)}; max_tokens={maxTokens}; bodyOmittedReason=ai_privacy";
        _logger.LogInformation("DeepSeek 请求开始 {SafeReq} url={Url}", safeReq, url);

        var client = _httpFactory.CreateClient(HttpClientName);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opt.ApiKey.Trim());
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        HttpResponseMessage resp;
        try
        {
            resp = await client.SendAsync(req, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _httpErrorLogger.Log("ai_send_or_transport_failed", "POST", url, "application/json", safeReq,
                false, null, null, null, sw.Elapsed.TotalMilliseconds, null, ex, omitAiBody: true);
            throw ThrowHelper.Api(url, safeReq, ex.Message, ex);
        }

        string respText;
        try
        {
            respText = await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception readEx)
        {
            sw.Stop();
            _httpErrorLogger.Log("ai_response_read_failed", "POST", url, "application/json", safeReq,
                true, (int)resp.StatusCode, resp.Content.Headers.ContentType?.ToString(), null,
                sw.Elapsed.TotalMilliseconds, null, readEx, omitAiBody: true);
            throw ThrowHelper.Api(url, safeReq, "响应读取失败", readEx);
        }

        sw.Stop();
        if (!resp.IsSuccessStatusCode)
        {
            var safeResp = Truncate(SanitizeSecrets(respText), 500);
            _httpErrorLogger.Log("ai_non_2xx", "POST", url, "application/json", safeReq,
                true, (int)resp.StatusCode, resp.Content.Headers.ContentType?.ToString(), safeResp,
                sw.Elapsed.TotalMilliseconds, ((int)resp.StatusCode).ToString(), null, omitAiBody: true);
            throw ThrowHelper.Api(url, safeReq, $"HTTP {(int)resp.StatusCode}: {safeResp}");
        }

        try
        {
            using var doc = JsonDocument.Parse(respText);
            var root = doc.RootElement;
            var choice = root.GetProperty("choices")[0].GetProperty("message");
            var content = choice.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;
            var toolCalls = new List<DeepSeekToolCall>();
            if (choice.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
            {
                foreach (var tc in tcs.EnumerateArray())
                {
                    toolCalls.Add(new DeepSeekToolCall
                    {
                        Id = tc.GetProperty("id").GetString() ?? "",
                        Type = tc.TryGetProperty("type", out var ty) ? ty.GetString() ?? "function" : "function",
                        Function = new DeepSeekFunctionCall
                        {
                            Name = tc.GetProperty("function").GetProperty("name").GetString() ?? "",
                            Arguments = tc.GetProperty("function").GetProperty("arguments").GetString() ?? "{}"
                        }
                    });
                }
            }

            DeepSeekUsage? usage = null;
            if (root.TryGetProperty("usage", out var u))
            {
                usage = new DeepSeekUsage
                {
                    PromptTokens = u.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : null,
                    CompletionTokens = u.TryGetProperty("completion_tokens", out var ct2) ? ct2.GetInt32() : null,
                    TotalTokens = u.TryGetProperty("total_tokens", out var tt) ? tt.GetInt32() : null
                };
            }

            return new DeepSeekChatResult
            {
                Content = content,
                ToolCalls = toolCalls,
                Usage = usage,
                RawSafeSummary = $"status=ok; tools={toolCalls.Count}; contentChars={(content?.Length ?? 0)}"
            };
        }
        catch (Exception ex)
        {
            _httpErrorLogger.Log("ai_json_parse_failed", "POST", url, "application/json", safeReq,
                true, (int)resp.StatusCode, resp.Content.Headers.ContentType?.ToString(), null,
                sw.Elapsed.TotalMilliseconds, "parse_error", ex, omitAiBody: true);
            throw ThrowHelper.Api(url, safeReq, "响应 JSON 解析失败", ex);
        }
    }

    private static string SanitizeSecrets(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text
            .Replace("Bearer ", "Bearer ***", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";
}
