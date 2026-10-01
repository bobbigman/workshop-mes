using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace ahu.MicrosoftMes.Common;

/// <summary>统一脱敏与截断（docs/2D §5）。先脱敏再按 UTF-8 字节截断。</summary>
public static class LogRedactor
{
    public const string Redacted = "***";
    public const string HeaderTraceId = "X-Trace-Id";

    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "pwd", "secret", "client_secret", "corpsecret", "token",
        "access_token", "refresh_token", "authorization", "cookie", "set-cookie",
        "api_key", "apikey", "api-key", "x-api-key"
    };

    private static readonly HashSet<string> SensitiveSqlHints = new(StringComparer.OrdinalIgnoreCase)
    {
        "sys_user", "password_hash", "password", "secret", "corp_secret", "api_key", "token"
    };

    private static readonly Regex ConnPwdRegex = new(
        @"(Password|Pwd)\s*=\s*[^;]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BearerRegex = new(
        @"Bearer\s+[A-Za-z0-9\-._~+/]+=*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string CurrentTraceId()
    {
        var id = Activity.Current?.Id;
        if (!string.IsNullOrWhiteSpace(id))
            return id!;
        return Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    }

    public static bool LooksTechnical(string? message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        if (message.Contains("SQL原文", StringComparison.OrdinalIgnoreCase)) return true;
        if (message.Contains("调用API失败", StringComparison.OrdinalIgnoreCase)) return true;
        if (message.Contains("SQL执行失败", StringComparison.OrdinalIgnoreCase)) return true;
        if (message.Contains("at ", StringComparison.Ordinal) && message.Contains(".cs:line", StringComparison.OrdinalIgnoreCase))
            return true;
        if (message.Contains("Stack Trace", StringComparison.OrdinalIgnoreCase)) return true;
        if (message.Contains("Password=", StringComparison.OrdinalIgnoreCase)) return true;
        if (message.Contains("corpsecret=", StringComparison.OrdinalIgnoreCase)) return true;
        if (message.Contains("access_token=", StringComparison.OrdinalIgnoreCase)) return true;
        if (message.Contains("Bearer ", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static string RedactText(string? text, int maxBytes)
    {
        if (text == null) return "";
        var s = ConnPwdRegex.Replace(text, "$1=" + Redacted);
        s = BearerRegex.Replace(s, "Bearer " + Redacted);
        return TruncateUtf8(s, maxBytes).Text;
    }

    public static string RedactUrl(string? url, int maxBytes = 4096)
    {
        if (string.IsNullOrEmpty(url)) return "";
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return TruncateUtf8(RedactText(url, maxBytes), maxBytes).Text;

            var builder = new UriBuilder(uri) { Fragment = "" };
            if (!string.IsNullOrEmpty(uri.Query))
            {
                var parts = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
                var kept = new List<string>(parts.Length);
                foreach (var part in parts)
                {
                    var idx = part.IndexOf('=');
                    var key = idx >= 0 ? Uri.UnescapeDataString(part[..idx]) : Uri.UnescapeDataString(part);
                    if (IsSensitiveKey(key))
                        kept.Add(Uri.EscapeDataString(key) + "=" + Redacted);
                    else
                        kept.Add(part);
                }
                builder.Query = string.Join("&", kept);
            }
            return TruncateUtf8(builder.Uri.ToString(), maxBytes).Text;
        }
        catch
        {
            return TruncateUtf8("[url-redact-failed]", 64).Text;
        }
    }

    public static object RedactHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in headers)
        {
            if (IsSensitiveKey(h.Key))
                dict[h.Key] = Redacted;
            else if (IsWhitelistHeader(h.Key))
                dict[h.Key] = string.Join(",", h.Value);
        }
        return dict;
    }

    public static TruncateResult RedactJsonOrForm(string? body, int maxBytes, bool omitAiBody = false)
    {
        if (omitAiBody)
        {
            return new TruncateResult
            {
                Text = "[omitted]",
                Truncated = true,
                OriginalBytes = body == null ? 0 : Encoding.UTF8.GetByteCount(body),
                KeptBytes = 9,
                Reason = "bodyOmittedReason=ai_privacy"
            };
        }

        if (string.IsNullOrEmpty(body))
            return new TruncateResult { Text = "", Truncated = false, OriginalBytes = 0, KeptBytes = 0, Reason = "" };

        try
        {
            using var doc = JsonDocument.Parse(body);
            var redacted = RedactJsonElement(doc.RootElement);
            var json = JsonSerializer.Serialize(redacted);
            return TruncateUtf8(json, maxBytes);
        }
        catch (JsonException)
        {
            // 表单：a=b&c=d
            if (body.Contains('=') && body.Contains('&'))
            {
                var parts = body.Split('&');
                var kept = new List<string>(parts.Length);
                foreach (var part in parts)
                {
                    var idx = part.IndexOf('=');
                    if (idx < 0) { kept.Add(part); continue; }
                    var key = Uri.UnescapeDataString(part[..idx]);
                    var val = idx + 1 < part.Length ? Uri.UnescapeDataString(part[(idx + 1)..]) : "";
                    kept.Add(Uri.EscapeDataString(key) + "=" +
                             (IsSensitiveKey(key) ? Redacted : val));
                }
                return TruncateUtf8(string.Join("&", kept), maxBytes);
            }

            var summary = $"[unparsed len={Encoding.UTF8.GetByteCount(body)} sha-prefix={StablePrefix(body)}]";
            return new TruncateResult
            {
                Text = summary,
                Truncated = true,
                OriginalBytes = Encoding.UTF8.GetByteCount(body),
                KeptBytes = Encoding.UTF8.GetByteCount(summary),
                Reason = "unsafe_or_unknown_content"
            };
        }
    }

    public static object RedactSqlParameters(IReadOnlyList<ParamSnap> parameters, string? commandText, int maxParamBytes)
    {
        var hideAll = commandText != null && SensitiveSqlHints.Any(h =>
            commandText.Contains(h, StringComparison.OrdinalIgnoreCase));
        var list = new List<object>();
        foreach (var p in parameters)
        {
            if (hideAll)
            {
                list.Add(new
                {
                    p.Name,
                    p.DbType,
                    p.Direction,
                    p.Size,
                    p.Precision,
                    p.Scale,
                    Value = Redacted,
                    ValueHiddenReason = "sensitive_command_heuristic"
                });
                continue;
            }

            var nameSensitive = IsSensitiveKey(p.Name.TrimStart('@'));
            object? value;
            string? reason = null;
            if (nameSensitive)
            {
                value = Redacted;
                reason = "sensitive_param_name";
            }
            else
                value = FormatParamValue(p.Value, maxParamBytes, out reason);

            list.Add(new
            {
                p.Name,
                p.DbType,
                p.Direction,
                p.Size,
                p.Precision,
                p.Scale,
                Value = value,
                ValueHiddenReason = reason
            });
        }
        return list;
    }

    public static string FormatExceptionChain(Exception ex, int maxBytes = 32_768)
    {
        var sb = new StringBuilder();
        var cur = ex;
        var depth = 0;
        while (cur != null && depth < 16)
        {
            if (depth > 0) sb.AppendLine("--- inner ---");
            sb.AppendLine(cur.GetType().FullName);
            sb.AppendLine(RedactText(cur.Message, 8_192));
            if (!string.IsNullOrEmpty(cur.StackTrace))
                sb.AppendLine(cur.StackTrace);
            if (cur is SqlException sqlEx)
            {
                foreach (SqlError err in sqlEx.Errors)
                    sb.AppendLine($"SqlError Number={err.Number} State={err.State} Class={err.Class} Proc={err.Procedure} Line={err.LineNumber} Msg={RedactText(err.Message, 2048)}");
            }
            cur = cur.InnerException;
            depth++;
        }
        return TruncateUtf8(sb.ToString(), maxBytes).Text;
    }

    public static TruncateResult TruncateUtf8(string text, int maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= maxBytes)
            return new TruncateResult { Text = text, Truncated = false, OriginalBytes = bytes.Length, KeptBytes = bytes.Length, Reason = "" };

        var slice = bytes.AsSpan(0, maxBytes);
        // 避免截断半个 UTF-8 字符
        while (slice.Length > 0 && (slice[^1] & 0xC0) == 0x80)
            slice = slice[..^1];
        var kept = Encoding.UTF8.GetString(slice) + $"…[truncated origBytes={bytes.Length}]";
        return new TruncateResult
        {
            Text = kept,
            Truncated = true,
            OriginalBytes = bytes.Length,
            KeptBytes = Encoding.UTF8.GetByteCount(kept),
            Reason = "max_bytes"
        };
    }

    public static bool IsSensitiveKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (SensitiveKeys.Contains(key)) return true;
        foreach (var s in SensitiveKeys)
        {
            if (key.Contains(s, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsWhitelistHeader(string key) =>
        key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Accept", StringComparison.OrdinalIgnoreCase)
        || key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)
        || key.Equals(HeaderTraceId, StringComparison.OrdinalIgnoreCase)
        || key.Equals("X-Request-Id", StringComparison.OrdinalIgnoreCase);

    private static object? RedactJsonElement(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new Dictionary<string, object?>();
                foreach (var p in el.EnumerateObject())
                    obj[p.Name] = IsSensitiveKey(p.Name) ? Redacted : RedactJsonElement(p.Value);
                return obj;
            case JsonValueKind.Array:
                return el.EnumerateArray().Select(RedactJsonElement).ToList();
            case JsonValueKind.String:
                return el.GetString();
            case JsonValueKind.Number:
                if (el.TryGetInt64(out var l)) return l;
                if (el.TryGetDouble(out var d)) return d;
                return el.GetRawText();
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Null: return null;
            default: return el.GetRawText();
        }
    }

    private static object? FormatParamValue(object? value, int maxBytes, out string? reason)
    {
        reason = null;
        if (value == null || value is DBNull) return null;
        if (value is byte[] bin)
        {
            reason = "binary_summary";
            return $"[binary bytes={bin.Length}]";
        }
        if (value is IEnumerable and not string)
        {
            reason = "collection_summary";
            var n = 0;
            foreach (var _ in (IEnumerable)value) n++;
            return $"[collection count={n}]";
        }
        var s = Convert.ToString(value) ?? "";
        var t = TruncateUtf8(s, maxBytes);
        if (t.Truncated) reason = t.Reason;
        return t.Text;
    }

    private static string StablePrefix(string s)
    {
        var hash = 0;
        foreach (var ch in s.Take(64))
            hash = (hash * 31) + ch;
        return hash.ToString("X8");
    }

    public sealed class TruncateResult
    {
        public string Text { get; set; } = "";
        public bool Truncated { get; set; }
        public int OriginalBytes { get; set; }
        public int KeptBytes { get; set; }
        public string Reason { get; set; } = "";
    }

    /// <summary>参数快照条目（在命令释放前采集）。</summary>
    public readonly struct ParamSnap
    {
        public string Name { get; init; }
        public string DbType { get; init; }
        public string Direction { get; init; }
        public int Size { get; init; }
        public byte Precision { get; init; }
        public byte Scale { get; init; }
        public object? Value { get; init; }
    }
}
