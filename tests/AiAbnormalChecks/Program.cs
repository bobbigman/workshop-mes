using ahu.MicrosoftMes.Services;
using ahu.MicrosoftMes.Common;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

var dispatcher = new AiToolDispatcher(null!, null!, null!, null!, null!,
    Options.Create(new AiOptions()), NullLogger<AiToolDispatcher>.Instance);
foreach (var mode in new[] { "progress", "guide", "support" })
{
    var hasTool = dispatcher.ToolsForMode(mode).Any(t => t.Function.Name == "search_abnormal");
    if (hasTool != (mode == "support")) throw new Exception($"工具声明越权: {mode}");
}
var checks = new[] {
    ("support", "{\"factoryId\":999}"),
    ("support", "{\"FactoryId\":999}"),
    ("progress", "{}"), ("guide", "{}"),
    ("support", "{\"status\":2}"),
    ("support", "{\"abnormalType\":4}"),
    ("support", "{\"page\":0}"),
    ("support", "{\"pageSize\":0}"),
    ("support", "{\"status\":\"0\"}"),
    ("support", "{\"page\":2147483647,\"pageSize\":20}")
};
foreach (var (mode, argsJson) in checks)
{
    try {
        await dispatcher.DispatchAsync("search_abnormal", argsJson,
            new AiSessionState { Mode = mode, FactoryId = 1 }, CancellationToken.None);
    }
    catch (BusinessException) { continue; }
    throw new Exception($"应拒绝参数或模式: {mode} {argsJson}");
}
Console.WriteLine("PASS: 三种模式工具声明、跨模式调用、工厂参数注入与非法查询参数（13项）");

AssistantExperienceChecks.Run();
