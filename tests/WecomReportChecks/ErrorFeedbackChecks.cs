using System.Reflection;
using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Mcp;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

public static class ErrorFeedbackChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var ctx = new McpRequestContext { CurrentFactoryId = 1 };
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { TraceIdentifier = "check-trace" } };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var user = new SysUser { Id = 7, FactoryId = 1, Status = 1, Role = 2 };
        var bindingCalls = 0;
        var binding = DispatchProxy.Create<IMcpWxBindService, Stub>();
        ((Stub)(object)binding).Call = (_, _) => { bindingCalls++; return Task.FromResult<SysUser?>(user); };
        var logger = new RecordingLogger();
        var reports = new FakeWecom();
        var taskService = DispatchProxy.Create<IWecomReportTaskService, Stub>();
        ((Stub)(object)taskService).Call = (_, _) => throw new InvalidOperationException("模拟查询失败 Password=do-not-log; Bearer private-value");
        var tools = new WecomReportMcpTools(reports, ctx, binding, http, config, cache, taskService, logger);
        static JsonElement Root(string value) => JsonDocument.Parse(value).RootElement.Clone();
        static int Code(JsonElement root) => root.GetProperty("code").GetInt32();
        static string Message(JsonElement root) => root.GetProperty("msg").GetString()!;
        static string Token(string value) => Root(value).GetProperty("data").GetProperty("confirmToken").GetString()!;
        foreach (var result in new[] { await tools.QueryTasks(), await tools.Preview("GD1", "包装", 10), await tools.Confirm("unused") })
        {
            var root = Root(result);
            check(Code(root) != 0 && Message(root).Contains("尚未启用"), "未启用经工具边界返回明确业务失败而不抛异常");
        }
        var readiness = Root(await tools.CheckReadiness());
        check(Code(readiness) == 0 && !readiness.GetProperty("data").GetProperty("ready").GetBoolean() && bindingCalls == 0,
            "未启用时仍可只读检查，缺身份不查询绑定");

        // 经过实际MCP SDK的工具参数绑定和文本结果转换，而不只直接调用C#方法。
        await using (var transport = new StreamServerTransport(new MemoryStream(), new MemoryStream(), "checks", NullLoggerFactory.Instance))
        await using (var server = McpServer.Create(transport, new McpServerOptions(), NullLoggerFactory.Instance))
        {
            var sdkTool = McpServerTool.Create(typeof(WecomReportMcpTools).GetMethod(nameof(WecomReportMcpTools.QueryTasks))!, tools);
            var request = new RequestContext<CallToolRequestParams>(server,
                new JsonRpcRequest { Id = new RequestId(1), Method = "tools/call" },
                new CallToolRequestParams { Name = "query_my_report_tasks" });
            var sdkResult = await sdkTool.InvokeAsync(request);
            var text = sdkResult.Content.OfType<TextContentBlock>().Single().Text;
            check(sdkResult.IsError is not true && Code(Root(text)) != 0 && Message(Root(text)).Contains("尚未启用"),
                "真实MCP SDK返回可读未启用内容，不变成未处理工具异常");
        }

        config["Mcp:WecomReport:Enabled"] = "true";
        check(Message(Root(await tools.QueryTasks())).Contains("缺少可靠"), "缺身份可读原因保留");
        http.HttpContext.Request.Headers["X-Mcp-Session-Id"] = "private-worker-identity";
        check(Message(Root(await tools.QueryTasks())).Contains("尚未核验"), "未核验可读原因保留");
        var key = $"Mcp:WecomReport:VerifiedUsers:1:{McpWxBindService.HashSession("private-worker-identity")}";
        config[key] = "8";
        check(Message(Root(await tools.QueryTasks())).Contains("不一致"), "绑定与核验不符可读原因保留");
        config[key] = "7";
        readiness = Root(await tools.CheckReadiness());
        check(readiness.GetProperty("data").GetProperty("ready").GetBoolean() && reports.Submitted == 0,
            "就绪检查全部通过也不提交报工");
        var visible = readiness.GetRawText();
        check(!visible.Contains("private-worker-identity") && !visible.Contains(McpWxBindService.HashSession("private-worker-identity")) &&
              !visible.Contains("userId", StringComparison.OrdinalIgnoreCase), "就绪检查不回显身份、哈希或员工ID");
        user.Status = 0;
        check(!Root(await tools.CheckReadiness()).GetProperty("data").GetProperty("ready").GetBoolean(), "停用员工就绪检查被阻断");
        user.Status = 1;
        ctx.CurrentFactoryId = null;
        check(!Root(await tools.CheckReadiness()).GetProperty("data").GetProperty("ready").GetBoolean(), "缺工厂就绪检查被阻断");
        ctx.CurrentFactoryId = 1;

        reports.PreviewError = ThrowHelper.BizCode("前道不足，请使用表单", 1002);
        var business = Root(await tools.Preview("GD1", "包装", 10));
        check(Code(business) == 1002 && Message(business).Contains("前道不足"), "服务业务错误原码和原因保留");
        reports.PreviewError = null;
        var queryError = Root(await tools.QueryTasks());
        check(Code(queryError) == 500 && queryError.GetProperty("data").GetProperty("traceId").GetString() == "check-trace" &&
              Message(queryError).Contains("查询") && !queryError.GetRawText().Contains("do-not-log"), "查询技术故障有追踪号，不暴露技术细节");
        check(logger.Entries.Any(x => x.Contains("mcp_report_error") && x.Contains("InvalidOperationException")) &&
              logger.Entries.All(x => !x.Contains("do-not-log") && !x.Contains("private-value")), "技术错误日志保留异常链并脱敏");
        reports.PreviewError = new InvalidOperationException("模拟预览故障");
        check(Code(Root(await tools.Preview("GD1", "包装", 10))) == 500 && reports.Submitted == 0, "预览技术故障返回失败且不写入");
        reports.PreviewError = null;
        var token = Token(await tools.Preview("GD1", "包装", 10));
        reports.ConfirmError = new InvalidOperationException("模拟提交结果未知");
        var confirmError = Root(await tools.Confirm(token));
        check(Code(confirmError) == 500 && Message(confirmError).Contains("结果待核实") &&
              confirmError.GetProperty("data").GetProperty("resultUnknown").GetBoolean(), "确认技术故障不谎称未入账，保留未知状态");
        reports.ConfirmError = null;
        check(Code(Root(await tools.Confirm(token))) == 0 && reports.Submitted == 1, "确认技术故障后可按原凭证继续，不需新建报工");
        reports.CancelError = new InvalidOperationException("模拟取消故障");
        var cancel = Root(await tools.Confirm(cancel: true));
        check(Code(cancel) == 500 && Message(cancel).Contains("取消结果待核实") &&
              cancel.GetProperty("data").GetProperty("resultUnknown").GetBoolean(), "取消故障不返回假成功");
    }

    private sealed class RecordingLogger : ILogger<WecomReportMcpTools>
    {
        public List<string> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(formatter(state, exception));
    }
}
