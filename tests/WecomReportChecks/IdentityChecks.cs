using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Controllers;
using ahu.MicrosoftMes.Mcp;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

public static class IdentityChecks
{
    public static async Task Run(Action<bool, string> check, Func<Func<Task>, string, Task> reject,
        Func<Func<Task<string>>, string, Task> rejectResult)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var clock = new Clock();
        var tickets = new ReportIdentityTickets(cache, clock);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Mcp:AuthBaseUrl"] = "https://example.invalid" }).Build();
        const string password = "only-web-page-pass";
        var directory = new Directory();
        directory.Users.AddRange(new[]
        {
            new SysUser { Id = 7, FactoryId = 1, Name = "胡忠谦", Phone = "13811112222", Role = 2, Password = PasswordHelper.Hash(password) },
            new SysUser { Id = 8, FactoryId = 1, Name = "张三", Phone = "13999112222", Role = 2 },
            new SysUser { Id = 9, FactoryId = 1, Name = "张三", Phone = "13788883333", Role = 2 },
            new SysUser { Id = 11, FactoryId = 1, Name = "停用员工", Phone = "13600000000", Role = 2, Status = 0 },
            new SysUser { Id = 12, FactoryId = 1, Name = "外部员工", Phone = "13500000000", Role = 2, UserType = UserTypeHelper.External }
        });
        var service = new ReportIdentityService(directory, tickets, config);
        static JsonElement Obj(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        static JsonElement Root(string value) => JsonDocument.Parse(value).RootElement.Clone();
        static string Lookup(JsonElement value) => value.GetProperty("lookupToken").GetString()!;
        static string Access(object value) => Obj(value).GetProperty("reportAccessToken").GetString()!;
        async Task<string> NewLookup() => Lookup(Obj(await service.IdentifyAsync(1, "胡忠谦", null)));
        var found = Obj(await service.IdentifyAsync(1, "胡忠谦", null));
        check(found.GetProperty("state").GetString() == "login_required" && found.GetProperty("userName").GetString() == "胡忠谦" &&
              found.GetProperty("authorizationUrl").GetString()!.StartsWith("https://example.invalid/#/h5/report-authorize?t="),
              "用户名唯一先定位并发登录验证链接，唯一匹配后仍需网页验证");
        check(!found.GetRawText().Contains("13811112222") && !found.GetRawText().Contains("Password"), "找人结果不展示完整手机号或密码");
        check(Obj(await service.IdentifyAsync(1, "张三", null)).GetProperty("state").GetString() == "need_phone_suffix", "姓名重名先询问手机号后6位");
        check(Obj(await service.IdentifyAsync(1, "胡", null)).GetProperty("state").GetString() == "need_phone_suffix", "姓名不模糊猜人，未命中询问手机号");
        check(Obj(await service.IdentifyAsync(1, null, "883333")).GetProperty("userName").GetString() == "张三", "手机号后6位唯一可定位");
        check(Obj(await service.IdentifyAsync(1, null, "112222")).GetProperty("state").GetString() == "need_full_phone", "后6位重号要求完整手机号");
        check(Obj(await service.IdentifyAsync(1, null, "000001")).GetProperty("state").GetString() == "need_full_phone", "后6位未命中要求完整手机号");
        check(Obj(await service.IdentifyAsync(1, null, "13811112222")).GetProperty("state").GetString() == "login_required", "完整手机号唯一匹配仍只要求登录验证");
        check(Obj(await service.IdentifyAsync(1, "停用员工", null)).GetProperty("state").GetString() != "login_required" &&
              Obj(await service.IdentifyAsync(1, "外部员工", null)).GetProperty("state").GetString() != "login_required", "停用及外部人员不进入授权候选");
        await reject(async () => await service.IdentifyAsync(1, null, "123"), "非法手机号格式拒绝");
        config["Mcp:AuthBaseUrl"] = "http://localhost:5000";
        await reject(async () => await service.IdentifyAsync(1, "胡忠谦", null), "拒绝向手机生成localhost或HTTP授权地址");
        config["Mcp:AuthBaseUrl"] = "https://example.invalid";
        var lookup = Lookup(found);
        await reject(async () => await service.VerifyAsync(1, lookup, "12345678"), "未网页登录确认不能兑换授权");
        await reject(async () => await service.InspectAsync(lookup, 1, 8), "他人登录不能读取和确认定位票据");
        await reject(async () => await service.InspectAsync(lookup, 2, 7), "其他工厂登录不能确认定位票据");
        await reject(async () => await service.ApproveAsync(lookup, 1, 7, "wrong-password"), "已有登录态但没有本账号密码也不能升级授权");
        var code = await service.ApproveAsync(lookup, 1, 7, password);
        check(code.Length == 8 && code.All(char.IsDigit), "本人登录并校验密码后产生8位验证码");
        await reject(async () => await service.VerifyAsync(2, lookup, code), "验证码不能跨MCP工厂兑换");
        var access = Access(await service.VerifyAsync(1, lookup, code));
        var actor = await service.ResolveAsync(1, access);
        check(actor.User.Id == 7 && actor.Grant.FactoryId == 1, "兑换凭证锁定已验证员工和工厂");
        await reject(async () => await service.VerifyAsync(1, lookup, code), "同验证码不能重复兑换");
        await reject(async () => await service.ResolveAsync(2, access), "短期凭证跨厂拒绝");
        directory.Users[0].Status = 0;
        await reject(async () => await service.ResolveAsync(1, access), "凭证签发后停用员工立即拒绝");
        directory.Users[0].Status = 1;

        var wrongLookup = await NewLookup();
        var rightCode = await service.ApproveAsync(wrongLookup, 1, 7, password);
        var wrongCode = rightCode == "00000000" ? "11111111" : "00000000";
        for (var i = 0; i < 5; i++)
            await reject(async () => await service.VerifyAsync(1, wrongLookup, wrongCode), "错误验证码被拒绝并累计次数");
        await reject(async () => await service.VerifyAsync(1, wrongLookup, rightCode), "验证码错误5次后正确码也不能复活旧票据");
        var expiredLookup = await NewLookup();
        var expiredCode = await service.ApproveAsync(expiredLookup, 1, 7, password);
        clock.Advance(TimeSpan.FromMinutes(6));
        await reject(async () => await service.VerifyAsync(1, expiredLookup, expiredCode), "验证码5分钟失效");
        clock.Advance(TimeSpan.FromMinutes(25));
        await reject(async () => await service.ResolveAsync(1, access), "报工凭证30分钟失效");

        // 新的有效网页登录授权，实际接入现有工具，不依赖任何企微身份头。
        var loginLookup = await NewLookup();
        var loginCode = await service.ApproveAsync(loginLookup, 1, 7, password);
        var loginAccess = Access(await service.VerifyAsync(1, loginLookup, loginCode));
        var bind = DispatchProxy.Create<IMcpWxBindService, Stub>();
        ((Stub)(object)bind).Call = (_, _) => throw new InvalidOperationException("网页登录授权不应查询企微绑定");
        var tasks = DispatchProxy.Create<IWecomReportTaskService, Stub>();
        ((Stub)(object)tasks).Call = (_, _) => Task.FromResult(new ReportTaskPage { Items = new() { new ReportTaskCandidate
            { Number = 1, OrderId = 100, OperationId = 200, OrderNo = "GD1", OperationName = "包装", PlanQty = 100 } } });
        var ctx = new McpRequestContext { CurrentFactoryId = 1 };
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var fake = new FakeWecom();
        var tools = new WecomReportMcpTools(fake, ctx, bind, http, config, cache, tasks, identity: service);
        await rejectResult(() => tools.QueryTasks(reportAccessToken: loginAccess), "即便已网页登录，关闭报工开关仍拒绝候选办理");
        var readiness = Root(await tools.CheckReadiness(loginAccess)).GetProperty("data");
        check(!readiness.GetProperty("ready").GetBoolean() && readiness.GetProperty("authenticationMode").GetString() == "verified_login",
            "接入检查识别网页登录模式且仍报告开关未启用");
        config["Mcp:WecomReport:Enabled"] = "true";
        var list = Root(await tools.QueryTasks(reportAccessToken: loginAccess)).GetProperty("data").GetProperty("candidateListToken").GetString();
        var preview = Root(await tools.Preview(goodQty: 50, candidateListToken: list, candidateNumber: 1, reportAccessToken: loginAccess));
        var confirm = preview.GetProperty("data").GetProperty("confirmToken").GetString();
        check(fake.LastPreview?.VerifiedLoginUserId == 7 && fake.LastPreview.McpOwner!.StartsWith("mcp-report-login:") && fake.Submitted == 0,
            "无企微头也能准确序号预览，员工ID仅由已验证凭证注入");
        await tools.Confirm(confirm, reportAccessToken: loginAccess);
        check(fake.Submitted == 1 && fake.LastConfirm?.McpUserId == 7, "网页登录授权接入原确认提交链路");
        await rejectResult(() => tools.Confirm(confirm, reportAccessToken: "invalid-token"), "错误登录凭证不降级到其他身份");
        check(Root(await tools.CheckReadiness(loginAccess)).GetProperty("data").GetProperty("ready").GetBoolean(), "无平台身份头时网页登录授权路径可通过接入检查");
        var dto = JsonSerializer.Deserialize<WecomReportPreviewDto>("{\"verifiedLoginUserId\":999,\"mcpOwner\":\"fake\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web));
        check(dto != null && dto.VerifiedLoginUserId == null && dto.McpOwner == null, "HTTP不能伪造内部登录认证员工ID和owner");
        check(typeof(ReportIdentityController).GetCustomAttribute<AuthorizeAttribute>() != null, "验证页接口强制JWT登录");
        var allowed = false;
        var workerHttp = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        { new Claim(JwtHelper.ClaimRole, "2"), new Claim(JwtHelper.ClaimUserId, "7"), new Claim(JwtHelper.ClaimFactoryId, "1") }, "checks")) };
        workerHttp.Request.Path = "/api/ReportIdentity/approve"; workerHttp.Request.Method = "POST";
        await new RoleGuardMiddleware(_ => { allowed = true; return Task.CompletedTask; }).InvokeAsync(workerHttp);
        check(allowed, "生产人员可进入本人授权接口，非管理员专属");
        var redacted = LogRedactor.RedactJsonOrForm("{\"lookupToken\":\"private-lookup\",\"reportAccessToken\":\"private-grant\",\"verificationCode\":\"12345678\"}", 4096).Text;
        check(!redacted.Contains("private-lookup") && !redacted.Contains("private-grant") && !redacted.Contains("12345678"), "新授权票据与验证码纳入通用日志脱敏");
        var rateLookup = await NewLookup();
        for (var i = 0; i < 5; i++) await reject(async () => await service.ApproveAsync(rateLookup, 1, 7, "wrong"), "授权密码失败被拒绝");
        await reject(async () => await service.ApproveAsync(await NewLookup(), 1, 7, password), "换定位票据也不能绕过账号密码失败限额");
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now += value;
    }
    private sealed class Directory : IReportIdentityDirectory
    {
        public List<SysUser> Users { get; } = new();
        public Task<List<SysUser>> FindAsync(long? factoryId, string? name, string? phone) => Task.FromResult(Users
            .Where(u => (factoryId == null || u.FactoryId == factoryId) && u.Status == 1 && u.UserType != UserTypeHelper.External && u.Role is 1 or 2 or 3)
            .Where(u => phone != null ? phone.Length == 6 ? u.Phone?.EndsWith(phone) == true : u.Phone == phone : u.Name == name)
            .Take(101).ToList());
        public Task<SysUser?> GetAsync(long factoryId, long userId) => Task.FromResult(Users.FirstOrDefault(u => u.FactoryId == factoryId && u.Id == userId));
        public Task<string> FactoryNameAsync(long factoryId) => Task.FromResult("测试工厂");
    }
}
