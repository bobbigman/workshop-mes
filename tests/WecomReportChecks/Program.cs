using System.Reflection;
using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Mcp;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

// 不连接数据库，不访问真实账套；覆盖新工具身份边界及真实确认服务并发/重放。
var count = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new InvalidOperationException(name);
    count++;
    Console.WriteLine($"PASS {name}");
}
async Task Reject(Func<Task> action, string name)
{
    try { await action(); }
    catch (BusinessException) { Check(true, name); return; }
    throw new InvalidOperationException($"未拒绝：{name}");
}
async Task RejectResult(Func<Task<string>> action, string name)
{
    using var json = JsonDocument.Parse(await action());
    Check(json.RootElement.GetProperty("code").GetInt32() != 0 &&
        !string.IsNullOrWhiteSpace(json.RootElement.GetProperty("msg").GetString()), name);
}
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Mcp:DemoSkipAuth"] = "true"
}).Build();
using var cache = new MemoryCache(new MemoryCacheOptions());
var context = new McpRequestContext { CurrentFactoryId = 1 };
var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
var user = new SysUser { Id = 7, FactoryId = 1, Status = 1, Role = 2, Name = "测试工人" };
var bind = DispatchProxy.Create<IMcpWxBindService, Stub>();
((Stub)(object)bind).Call = (_, _) => Task.FromResult<SysUser?>(user);
var fake = new FakeWecom();
var tools = new WecomReportMcpTools(fake, context, bind, http, config, cache);
await RejectResult(() => tools.Preview("GD1", "包装", 10), "默认关闭，即使查询演示模式开启");
config["Mcp:WecomReport:Enabled"] = "true";
await RejectResult(() => tools.Preview("GD1", "包装", 10), "缺平台身份被拒绝");
http.HttpContext.Request.Headers["X-Mcp-Session-Id"] = "worker-a";
await RejectResult(() => tools.Preview("GD1", "包装", 10), "仅绑定未管理员核验被拒绝");
var verifiedKey = $"Mcp:WecomReport:VerifiedUsers:1:{McpWxBindService.HashSession("worker-a")}";
config[verifiedKey] = "8";
await RejectResult(() => tools.Preview("GD1", "包装", 10), "绑定员工与核验员工不一致被拒绝");
config[verifiedKey] = "7";
string Token(string json) => JsonDocument.Parse(json).RootElement.GetProperty("data").GetProperty("confirmToken").GetString()!;
var first = Token(await tools.Preview("GD1", "包装", 10));
var second = Token(await tools.Preview("GD1", "包装", 11));
Check(fake.Cancelled.Contains(first), "新预览作废旧凭证");
await RejectResult(() => tools.Confirm(first), "旧凭证不能提交");
await tools.Confirm(second, cancel: true);
Check(fake.Submitted == 0, "取消不提交");
await RejectResult(() => tools.Confirm(second), "取消后凭证无效");
var current = Token(await tools.Preview("GD1", "包装", 12));
context.CurrentFactoryId = 2;
await RejectResult(() => tools.Confirm(current), "跨厂被拒绝");
context.CurrentFactoryId = 1;
http.HttpContext.Request.Headers["X-Mcp-Session-Id"] = "worker-b";
config[$"Mcp:WecomReport:VerifiedUsers:1:{McpWxBindService.HashSession("worker-b")}"] = "7";
await RejectResult(() => tools.Confirm(current), "同员工另一身份会话不能确认");
http.HttpContext.Request.Headers["X-Mcp-Session-Id"] = "worker-a";
user.Status = 0;
await RejectResult(() => tools.Confirm(current), "预览后停用员工不能确认");
user.Status = 1;
await tools.Confirm(current);
Check(fake.Submitted == 1 && fake.LastConfirm?.McpUserId == 7, "本人确认携带核验员工");

// 注入待确认对象，实际执行 WecomReportService 的保护与并发逻辑。
var submitted = 0;
var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var reports = DispatchProxy.Create<IReportService, Stub>();
((Stub)(object)reports).Call = (method, args) =>
{
    if (method.Name != "SubmitAsync") throw new InvalidOperationException(method.Name);
    return Submit();
};
async Task<ApiResult<object?>> Submit()
{
    Interlocked.Increment(ref submitted);
    entered.TrySetResult();
    await release.Task;
    return ApiResult<object?>.Ok(new { id = 123 });
}
var service = new WecomReportService(null!, reports, cache, config, NullLogger<WecomReportService>.Instance);
void Seed(string token, string? owner = "owner-a")
{
    var type = typeof(WecomReportService).GetNestedType("PendingConfirm", BindingFlags.NonPublic)!;
    var pending = Activator.CreateInstance(type)!;
    void Set(string name, object? value) => type.GetProperty(name)!.SetValue(pending, value);
    Set("FactoryId", 1L); Set("UserId", 7L); Set("McpOwner", owner);
    Set("OrderNo", "GD1"); Set("OperationName", "包装"); Set("GoodQty", 10);
    cache.Set("wecom-report-confirm:" + token, pending, TimeSpan.FromMinutes(1));
}
WecomReportConfirmDto Dto(string token, string? owner = "owner-a", long id = 7) => new()
{ ConfirmToken = token, McpOwner = owner, McpUserId = id };
Seed("token-a");
await Reject(async () => await service.ConfirmAsync(Dto("token-a"), 2), "真实确认服务拒绝跨厂");
await Reject(async () => await service.ConfirmAsync(Dto("token-a", "owner-b"), 1), "真实确认服务拒绝跨会话");
await Reject(async () => await service.ConfirmAsync(Dto("token-a", id: 8), 1), "真实确认服务拒绝换员工");
await Reject(async () => await service.ConfirmAsync(Dto("token-a", null), 1), "普通HTTP不能使用MCP凭证绕过本人核对");
var running = service.ConfirmAsync(Dto("token-a"), 1);
await entered.Task;
await Reject(async () => await service.ConfirmAsync(Dto("token-a"), 1), "并发确认被阻止且不进入第二次提交");
release.SetResult();
await running;
await service.ConfirmAsync(Dto("token-a"), 1);
Check(submitted == 1, "完成后重复确认回放，实际只提交一次");
Seed("token-cancel");
service.CancelMcpPreview("token-cancel", 1, "owner-a");
await Reject(async () => await service.ConfirmAsync(Dto("token-cancel"), 1), "真实服务取消后无法确认");
await Reject(async () => await service.ConfirmAsync(Dto("expired"), 1), "缺失或过期凭证拒绝");
Check(typeof(WecomReportMcpTools).GetMethods().Count(m => m.GetCustomAttributes().Any(a => a.GetType().Name == "McpServerToolAttribute")) == 6,
    "找人、验证码验证、接入检查、候选、预览、确认六个工具具备MCP自动发现标记");
await CandidateChecks.Run(Check, Reject, RejectResult);
await ErrorFeedbackChecks.Run(Check);
await IdentityChecks.Run(Check, Reject, RejectResult);
await MultiFactoryIdentityChecks.Run(Check, Reject);
await ReportEntryChecks.Run(Check);
Console.WriteLine($"全部 {count} 项通过；未连接数据库，未验证真实落库和企微现场。");

public class Stub : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Call { get; set; } = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!, args);
}
public class FakeWecom : IWecomReportService
{
    public Exception? PreviewError { get; set; }
    public Exception? ConfirmError { get; set; }
    public Exception? CancelError { get; set; }
    public HashSet<string> Cancelled { get; } = new();
    public int Submitted { get; private set; }
    public long LastFactoryId { get; private set; }
    public WecomReportConfirmDto? LastConfirm { get; private set; }
    public WecomReportPreviewDto? LastPreview { get; private set; }
    public Task<ApiResult<WecomReportPreviewResultDto>> PreviewAsync(WecomReportPreviewDto dto, long factoryId)
    {
        if (PreviewError != null) throw PreviewError;
        LastPreview = dto; LastFactoryId = factoryId;
        return Task.FromResult(ApiResult<WecomReportPreviewResultDto>.Ok(new() { ConfirmToken = Guid.NewGuid().ToString("N"), ExpireMinutes = 5 }));
    }
    public Task<ApiResult<object?>> ConfirmAsync(WecomReportConfirmDto dto, long factoryId)
    {
        if (ConfirmError != null) throw ConfirmError;
        Submitted++; LastConfirm = dto; LastFactoryId = factoryId;
        return Task.FromResult(ApiResult<object?>.Ok(new { id = 1 }));
    }
    public void CancelMcpPreview(string token, long factoryId, string owner)
    {
        if (CancelError != null) throw CancelError;
        Cancelled.Add(token);
    }
}
