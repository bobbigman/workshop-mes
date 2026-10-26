using System.Security.Claims;
using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Controllers;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace WorkshopMes.Tests;

public class WorkerHelpTests
{
    private static IOptions<AiOptions> OptionsForTests() => Options.Create(new AiOptions
        { Enabled = true, ApiKey = "fixture", Model = "fixture", MaxSessionsPerUser = 2 });

    private sealed class Model : IDeepSeekClient
    {
        public int Calls;
        public bool MaliciousTool;
        public bool Fail;
        public IReadOnlyList<DeepSeekChatMessage> Last = Array.Empty<DeepSeekChatMessage>();
        public Task<DeepSeekChatResult> ChatAsync(IReadOnlyList<DeepSeekChatMessage> messages,
            IReadOnlyList<DeepSeekToolDef>? tools, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Last = messages; Calls++;
            if (Fail) throw new InvalidOperationException("fixture-model-failed");
            if (MaliciousTool && Calls == 1) return Task.FromResult(new DeepSeekChatResult
                { ToolCalls = new() { new() { Id = "bad", Function = new() { Name = "search_work_orders", Arguments = "{}" } } } });
            return Task.FromResult(new DeepSeekChatResult { Content = "从工作台点击扫码报工，核对工单后填写数量。" });
        }
        public Task<DeepSeekChatResult> ConnectionTestAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private static (AiAssistantService service, AiSessionStore store, Model model, AiToolDispatcher tools) Build()
    {
        var opt = OptionsForTests(); var store = new AiSessionStore(opt); var model = new Model();
        var docs = new SupportDocumentService(opt, NullLogger<SupportDocumentService>.Instance);
        // 所有业务依赖故意为空；只要错误调用业务工具，测试立即失败。
        var tools = new AiToolDispatcher(null!, null!, null!, docs, null!, null!, null!, opt, NullLogger<AiToolDispatcher>.Instance);
        return (new AiAssistantService(opt, model, store, tools, null!, NullLogger<AiAssistantService>.Instance), store, model, tools);
    }

    [Theory]
    [InlineData("怎么扫码报工", false)]
    [InlineData("报工失败", false)]
    [InlineData("工资明细在哪看", false)]
    [InlineData("工单进度怎么看", false)]
    [InlineData("我的工资多少", true)]
    [InlineData("帮我通过复核", true)]
    [InlineData("A001工单完成多少", true)]
    public void ScopePolicy(string question, bool outside) => Assert.Equal(outside, WorkerHelpPolicy.IsDataOrActionRequest(question));

    [Fact]
    public async Task ToolAllowlist_RejectsBusinessAndForbiddenParameters()
    {
        var (_, _, _, tools) = Build(); var s = new AiSessionState { Mode = WorkerHelpPolicy.Mode };
        Assert.Single(tools.ToolsForMode(s.Mode));
        foreach (var name in new[] { "search_work_orders", "search_abnormal", "get_work_order_report_evidence", "get_work_order_payable", "get_completed_work_orders_payable", "resolve_product", "get_production_guide" })
            await Assert.ThrowsAsync<BusinessException>(() => tools.DispatchAsync(name, "{}", s, default));
        await Assert.ThrowsAsync<BusinessException>(() => tools.DispatchAsync("search_support_docs", "{\"query\":\"扫码\",\"factoryId\":9}", s, default));
    }

    [Fact]
    public async Task OnlyPublishedResourcesReachModel_EvenWhenCustomDirectoryHasSecrets()
    {
        var (service, _, model, _) = Build();
        var result = await service.SendMessageAsync(new() { Mode = WorkerHelpPolicy.Mode, Message = "怎么扫码报工" }, 1, 1, default);
        Assert.NotEmpty(result.Data!.Sources);
        Assert.All(result.Data.Sources, s => Assert.True(s.SourceId.StartsWith("worker:mobile#") || s.SourceId.StartsWith("worker:faq#")));
        Assert.Contains(model.Last, m => m.Role == "tool" && m.Content!.Contains("worker:mobile#"));
        Assert.Null(result.Data.WorkOrders); Assert.Null(result.Data.ReportEvidence);
        var options = OptionsForTests(); options.Value.SupportDocs = new() { Enabled = true, RootPath = "Z:/not-allowed-internal-directory" };
        var docs = new SupportDocumentService(options, NullLogger<SupportDocumentService>.Instance);
        Assert.Equal("ready", docs.SearchWorkerHelp("扫码").State); // 目录不可用不影响固定发布手册。
        Assert.All(docs.SearchWorkerHelp("密码").Sources, s => Assert.StartsWith("worker:", s.SourceId));
    }

    [Fact]
    public async Task DataRequest_DoesNotCallModel_AndMaliciousModelCannotDispatchBusinessTools()
    {
        var (service, _, model, _) = Build();
        var scope = await service.SendMessageAsync(new() { Mode = WorkerHelpPolicy.Mode, Message = "我的工资多少" }, 1, 1, default);
        Assert.Equal(WorkerHelpPolicy.ScopeMessage, scope.Data!.Answer); Assert.Equal(0, model.Calls);
        model.MaliciousTool = true;
        await service.SendMessageAsync(new() { Mode = WorkerHelpPolicy.Mode, Message = "怎么扫码报工" }, 1, 1, default);
        Assert.Contains(model.Last, m => m.Role == "tool" && m.Content != null && m.ToolCallId == "bad" && JsonDocument.Parse(m.Content).RootElement.TryGetProperty("error", out var error) && error.GetString()!.Contains("当前模式下不可用"));
    }

    [Fact]
    public async Task SessionModeAndOwnershipAreEnforced_AndFailuresReleaseBusy()
    {
        var (service, store, model, _) = Build(); var admin = store.Create(1, 1, "support");
        await Assert.ThrowsAsync<BusinessException>(() => service.SendMessageAsync(new() { Mode = WorkerHelpPolicy.Mode, Message = "怎么扫码", ConversationId = admin.Id }, 1, 1, default));
        Assert.Throws<BusinessException>(() => store.DeleteOwned(admin.Id, 1, 1, WorkerHelpPolicy.Mode));
        var response = await service.SendMessageAsync(new() { Mode = WorkerHelpPolicy.Mode, Message = "怎么扫码报工" }, 1, 1, default);
        var id = response.Data!.ConversationId;
        foreach (var (user, factory) in new[] { (2L, 1L), (1L, 2L) })
        {
            await Assert.ThrowsAsync<BusinessException>(() => service.SendMessageAsync(new() { Mode = WorkerHelpPolicy.Mode, Message = "怎么扫码", ConversationId = id }, user, factory, default));
            Assert.Throws<BusinessException>(() => store.DeleteOwned(id, factory, user, WorkerHelpPolicy.Mode));
        }
        model.Fail = true;
        await Assert.ThrowsAsync<BusinessException>(() => service.SendMessageAsync(new() { Mode = WorkerHelpPolicy.Mode, Message = "怎么扫码报工", ConversationId = id }, 1, 1, default));
        Assert.False(store.GetOwned(id, 1, 1).Busy);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<Exception>(() => service.SendMessageAsync(new() { Mode = WorkerHelpPolicy.Mode, Message = "怎么扫码报工", ConversationId = id }, 1, 1, cancel.Token));
        Assert.False(store.GetOwned(id, 1, 1).Busy);
    }

    [Fact]
    public async Task ActiveIdentity_CheckedAgainstCurrentDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Factories.Add(new SysFactory { Id = 1, FactoryCode = "fixture", FactoryName = "fixture" });
        db.Users.AddRange(Enumerable.Range(1, 3).Select(i => new SysUser { Id = i, FactoryId = 1, Role = (byte)i, Account = "fixture" + i, Name = "fixture", Password = "fixture" }));
        await db.SaveChangesAsync();
        var (assistant, store, _, _) = Build(); var service = new WorkerHelpService(db, assistant, store);
        foreach (var id in new long[] { 1, 2, 3 }) await service.StatusAsync(id, 1, default);
        await Assert.ThrowsAsync<BusinessException>(() => service.StatusAsync(2, 9, default));
        db.Users.Single(u => u.Id == 2).Status = 0; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessException>(() => service.StatusAsync(2, 1, default));
        var json = JsonSerializer.Serialize((await service.StatusAsync(1, 1, default)).Data);
        Assert.DoesNotContain("Model", json); Assert.DoesNotContain("ApiKey", json);
    }

    [Fact]
    public async Task GatewayOnlyAllowsThreeEndpoints_OriginalAdminAiStillForbidden()
    {
        foreach (var role in new[] { "2", "3" })
        foreach (var (path, method, allowed) in new[] { ("/api/worker-help/status", "GET", true), ("/api/worker-help/messages", "POST", true),
            ("/api/worker-help/conversations/" + Guid.NewGuid().ToString("N"), "DELETE", true),
            ("/api/worker-help/connection-test", "POST", false), ("/api/worker-help/status", "PUT", false),
            ("/api/ai-assistant/messages", "POST", false), ("/api/ai-assistant/status", "GET", false) })
        {
            var ctx = new DefaultHttpContext(); ctx.Request.Path = path; ctx.Request.Method = method;
            ctx.Response.Body = new MemoryStream(); ctx.User = new(new ClaimsIdentity(new[] { new Claim(JwtHelper.ClaimRole, role) }, "fixture"));
            var called = false; await new RoleGuardMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(ctx);
            Assert.Equal(allowed, called);
        }
        Assert.NotEmpty(typeof(WorkerHelpController).GetCustomAttributes(typeof(AuthorizeAttribute), true));
        foreach (var property in new[] { "mode", "userId", "factoryId" })
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WorkerHelpRequest>($"{{\"{property}\":\"support\"}}"));
    }

    [Fact]
    public async Task HttpBoundary_AnonymousDenied_ValidRolesAllowed_ExtraFieldsAndAdminApiDenied()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Factories.Add(new SysFactory { Id = 1, FactoryCode = "fixture", FactoryName = "fixture" });
        db.Users.AddRange(Enumerable.Range(1, 3).Select(i => new SysUser { Id = i, FactoryId = 1, Role = (byte)i,
            Account = "http-fixture" + i, Name = "fixture", Password = "fixture" }));
        await db.SaveChangesAsync();
        var (assistant, store, _, _) = Build();
        var jwt = new JwtOptions { Secret = "worker-help-isolated-http-fixture-key-273", Issuer = "fixture", Audience = "fixture" };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(db); builder.Services.AddSingleton<IAiAssistantService>(assistant);
        builder.Services.AddSingleton(store); builder.Services.AddScoped<WorkerHelpService>();
        builder.Services.AddControllers().AddApplicationPart(typeof(WorkerHelpController).Assembly);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidIssuer = jwt.Issuer,
                ValidateAudience = true, ValidAudience = jwt.Audience, ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)), ValidateLifetime = true };
        });
        builder.Services.AddAuthorization();
        await using var app = builder.Build(); app.Urls.Add("http://127.0.0.1:0");
        app.Use(async (context, next) =>
        {
            try { await next(); }
            catch (Exception ex) { context.Response.StatusCode = 500; await context.Response.WriteAsync(ex.ToString()); }
        });
        app.UseAuthentication(); app.UseMiddleware<RoleGuardMiddleware>(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        try
        {
            var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(url) };
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/worker-help/status")).StatusCode);
            foreach (byte role in new byte[] { 1, 2, 3 })
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtHelper.CreateToken(jwt, role, 1, role, "fixture"));
                var status = await client.GetAsync("/api/worker-help/status"); Assert.True(status.IsSuccessStatusCode, await status.Content.ReadAsStringAsync());
                Assert.DoesNotContain("model", await status.Content.ReadAsStringAsync());
                var invalid = await client.PostAsJsonAsync("/api/worker-help/messages", new { message = "怎么扫码", mode = "support" });
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
                if (role != 1) Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ai-assistant/status")).StatusCode);
            }
        }
        finally { await app.StopAsync(); }
    }
}
