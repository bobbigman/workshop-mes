using System.Diagnostics;

namespace ahu.MicrosoftMes.Common;

/// <summary>请求入口建立服务端可控 TraceId；忽略不可信客户端关联值。</summary>
public class TraceIdMiddleware
{
    public const string ItemKey = "MesTraceId";
    public const string HeaderName = LogRedactor.HeaderTraceId;

    private readonly RequestDelegate _next;

    public TraceIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        // 不信任客户端传来的 X-Trace-Id / traceparent 作为权威值；可复用 Activity
        Activity.Current ??= new Activity("http.request").Start();
        var traceId = Activity.Current.Id ?? Activity.Current.TraceId.ToString();
        context.Items[ItemKey] = traceId;
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(HeaderName))
                context.Response.Headers[HeaderName] = traceId;
            return Task.CompletedTask;
        });

        using (Serilog.Context.LogContext.PushProperty("TraceId", traceId))
        {
            await _next(context);
        }
    }

    public static string Get(HttpContext ctx) =>
        ctx.Items.TryGetValue(ItemKey, out var v) && v is string s && !string.IsNullOrEmpty(s)
            ? s
            : LogRedactor.CurrentTraceId();
}
