using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Mcp;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Reflection;
using ahu.MicrosoftMes.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;

namespace WorkshopMes.Tests;

internal static class IdentityTestDatabase
{
    private static readonly string Hash = PasswordHelper.Hash("test-password");
    public static AppDbContext Open()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options);
        db.Database.OpenConnection(); db.Database.EnsureCreated();
        db.Factories.AddRange(new SysFactory { Id = 1, FactoryCode = "A", FactoryName = "测试A" },
            new SysFactory { Id = 2, FactoryCode = "B", FactoryName = "测试B" });
        db.Users.AddRange(Enumerable.Range(1, 3).Select(i => new SysUser { Id = i, FactoryId = 1,
            Role = 1, Name = "甲" + i, Account = "user" + i, Phone = "1380000000" + i,
            Password = Hash }));
        db.SaveChanges(); return db;
    }
}

public class TenantIsolation426Tests
{
    private static IConfiguration Config(params (string key, string value)[] values) => new ConfigurationBuilder()
        .AddInMemoryCollection(values.ToDictionary(x => x.key, x => (string?)x.value)).Build();

    [Theory]
    [InlineData(0, 1, 1)] [InlineData(1, 2, 1)] [InlineData(1, 1, 2)]
    public async Task CurrentIdentity_RejectsDisabledWrongFactoryOrDemoted(int status, long factory, byte role)
    {
        await using var db = IdentityTestDatabase.Open();
        var user = await db.Users.FindAsync(1L); user!.Status = (byte)status; user.Role = role; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessException>(() => CurrentAccountAccess.RequireAsync(db, 1, factory));
    }

    [Fact]
    public async Task CurrentIdentity_CleanedFactoryAndWorkerScope()
    {
        await using var db = IdentityTestDatabase.Open();
        db.Users.Single(u => u.Id == 1).Role = 2; await db.SaveChangesAsync();
        Assert.Equal(2, (await CurrentAccountAccess.RequireAsync(db, 1, 1, false)).Role);
        db.Factories.Single(f => f.Id == 1).TrialCleanedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessException>(() => CurrentAccountAccess.RequireAsync(db, 1, 1, false));
    }

    [Fact]
    public void QueryTickets_SeparateScopeReplayCrossFactoryAndRevocation()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var query = new QueryIdentityTickets(cache); var report = new ReportIdentityTickets(cache);
        var lookup = query.Create(1, 1, "甲", "A");
        Assert.Throws<BusinessException>(() => query.Approve(lookup, 2, 1));
        Assert.Throws<BusinessException>(() => report.Read(lookup));
        var code = query.Approve(lookup, 1, 1);
        Assert.Throws<BusinessException>(() => query.Redeem(lookup, 2, code));
        var grant = query.Redeem(lookup, 1, code);
        Assert.Throws<BusinessException>(() => query.Redeem(lookup, 1, code));
        Assert.Throws<BusinessException>(() => report.Resolve(grant.Token, 1));
        Assert.Throws<BusinessException>(() => query.Resolve(grant.Token, 2));
        Assert.Equal(1, query.Resolve(grant.Token, 1).UserId);
        query.Revoke(1, 1);
        Assert.Throws<BusinessException>(() => query.Resolve(grant.Token, 1));
        var next = query.Create(1, 1, "甲", "A");
        var fresh = query.Redeem(next, 1, query.Approve(next, 1, 1));
        Assert.Equal(1, query.Resolve(fresh.Token, 1).UserId);
    }

    [Fact]
    public void QueryTickets_FiveWrongCodesInvalidate()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions()); var tickets = new QueryIdentityTickets(cache);
        var lookup = tickets.Create(1, 1, "甲", "A"); var code = tickets.Approve(lookup, 1, 1);
        for (var i = 0; i < 5; i++) Assert.Throws<BusinessException>(() => tickets.Redeem(lookup, 1, "wrong"));
        Assert.Throws<BusinessException>(() => tickets.Redeem(lookup, 1, code));
    }

    [Fact]
    public async Task QueryIdentity_WebPasswordAndCurrentUserAreRequired()
    {
        await using var db = IdentityTestDatabase.Open(); using var cache = new MemoryCache(new MemoryCacheOptions());
        var tickets = new QueryIdentityTickets(cache);
        var svc = new McpQueryIdentityService(db, tickets, Config(("Mcp:AuthBaseUrl", "https://example.invalid")));
        var start = JsonSerializer.SerializeToElement(await svc.StartAsync(1, "13800000001"));
        var lookup = start.GetProperty("lookupToken").GetString()!;
        await Assert.ThrowsAsync<BusinessException>(() => svc.InspectAsync(lookup, 1, 2));
        await Assert.ThrowsAsync<BusinessException>(() => svc.ApproveAsync(lookup, 1, 1, "wrong"));
        var code = await svc.ApproveAsync(lookup, 1, 1, "test-password");
        var result = JsonSerializer.SerializeToElement(await svc.VerifyAsync(1, lookup, code));
        var access = result.GetProperty("queryAccessToken").GetString()!;
        Assert.Equal(1, (await svc.ResolveAsync(1, access)).Id);
        await Assert.ThrowsAsync<BusinessException>(() => svc.ResolveAsync(2, access));
        db.Users.Single(u => u.Id == 1).Status = 0; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessException>(() => svc.ResolveAsync(1, access));
    }

    [Theory]
    [InlineData(null, true)] [InlineData("wrong", true)] [InlineData("test-proxy-secret-at-least-32-characters", false)]
    public void PlatformHeader_RequiresExplicitGatewayProof(string? proof, bool enabled)
    {
        var ctx = new DefaultHttpContext(); if (proof != null) ctx.Request.Headers["X-Mcp-Identity-Key"] = proof;
        Assert.False(McpQueryIdentityService.HasTrustedIdentity(ctx.Request, Config(("Mcp:Identity:Enabled", enabled.ToString()),
            ("Mcp:Identity:TrustedProxyKey", "test-proxy-secret-at-least-32-characters"))));
    }

    private sealed class License : ILicenseTierService
    {
        public long? Factory;
        public Task<LicenseTier> GetTierAsync(long f) => Task.FromResult(LicenseTier.Flagship);
        public Task<bool> CanUseAsync(long f, LicenseFeature x) => Task.FromResult(f == 2);
        public Task EnsureFeatureAsync(long f, LicenseFeature x) { Factory = f; if (f != 2) throw ThrowHelper.BizUser("未授权"); return Task.CompletedTask; }
    }

    [Theory]
    [InlineData(null, 200)] [InlineData("2", 200)] [InlineData("1", 403)]
    [InlineData("", 400)] [InlineData("garbage", 400)] [InlineData("0", 400)] [InlineData("2,1", 400)]
    public async Task McpMiddleware_ActualKeyFactoryAndStrictLabels(string? label, int expected)
    {
        await using var db = IdentityTestDatabase.Open(); var license = new License(); var current = new McpRequestContext();
        using var services = new ServiceCollection().AddSingleton(db).AddSingleton<IMcpRequestContext>(current)
            .AddSingleton<ILicenseTierService>(license).BuildServiceProvider();
        var ctx = new DefaultHttpContext { RequestServices = services }; ctx.Request.Path = "/mcp";
        ctx.Request.Headers.Authorization = "Bearer fixture-token"; ctx.Response.Body = new MemoryStream();
        if (label != null) ctx.Request.Headers["factoryid"] = label;
        var called = false;
        await new McpTokenMiddleware(_ => { called = true; return Task.CompletedTask; },
            Config(("Mcp:Token", "fixture-token"), ("Mcp:FactoryId", "2")), NullLogger<McpTokenMiddleware>.Instance).InvokeAsync(ctx);
        Assert.Equal(expected, ctx.Response.StatusCode); Assert.Equal(expected == 200, called);
        if (called) { Assert.Equal(2, current.CurrentFactoryId); Assert.Equal(2, license.Factory); }
    }

    [Fact]
    public async Task McpFactoryOptions_OnlyCurrentFactory()
    {
        await using var db = IdentityTestDatabase.Open();
        var svc = new McpKeyService(db, NullLogger<McpKeyService>.Instance);
        Assert.Equal(1, Assert.Single((await svc.ListFactoriesAsync(1)).Data!).Id);
        Assert.Empty((await svc.ListFactoriesAsync(99)).Data!);
    }

    [Fact]
    public async Task CorruptAssociations_RejectForeignProductRouteAndOperation()
    {
        await using var db = IdentityTestDatabase.Open();
        db.Products.Add(new BaseProduct { Id = 1, FactoryId = 2, Code = "foreign", Name = "外厂秘密" });
        db.WorkOrders.Add(new ProdWorkOrder { Id = 1, FactoryId = 1, OrderNo = "same-no", ProductId = 1 });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessException>(() => TenantDataGuard.WorkOrdersAsync(db, 1));
        var product = await db.Products.FindAsync(1L); product!.FactoryId = 1;
        db.Operations.Add(new BaseOperation { Id = 1, FactoryId = 2, Code = "foreign-op", Name = "外厂工艺" });
        db.WorkOrderOperations.Add(new ProdWorkOrderOperation { WorkOrderId = 1, OperationId = 1 });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessException>(() => TenantDataGuard.WorkOrdersAsync(db, 1, new[] { 1L }));
        db.Routings.Add(new BaseRouting { Id = 1, FactoryId = 2, Code = "foreign-route", Name = "外厂路线" });
        product.RoutingId = 1; await db.SaveChangesAsync();
        var knowledge = new KnowledgeService(db, null!);
        await Assert.ThrowsAsync<BusinessException>(() => knowledge.QueryProductionGuideAsync("foreign", null, null, 1));
        db.Routings.Single(r => r.Id == 1).FactoryId = 1;
        db.RoutingSteps.Add(new BaseRoutingStep { RoutingId = 1, OperationId = 1, Seq = 1 });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessException>(() => knowledge.QueryProductionGuideAsync("foreign", null, null, 1));
    }

    [Fact]
    public async Task OldBindingAndDemoCannotAuthorize_TrustedGatewayAndUnbindWork()
    {
        await using var db = IdentityTestDatabase.Open(); using var cache = new MemoryCache(new MemoryCacheOptions());
        const string platform = "platform-user-1", proof = "test-proxy-secret-at-least-32-characters";
        var config = Config(("Mcp:DemoSkipAuth", "true"), ("Mcp:Identity:Enabled", "true"),
            ("Mcp:Identity:TrustedProxyKey", proof),
            ($"Mcp:WecomReport:VerifiedUsers:1:{McpWxBindService.HashSession(platform)}", "1"));
        var tickets = new QueryIdentityTickets(cache); var query = new McpQueryIdentityService(db, tickets, config);
        using var services = new ServiceCollection().AddSingleton(query).BuildServiceProvider();
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { RequestServices = services } };
        http.HttpContext.Request.Headers["X-Mcp-Session-Id"] = platform;
        db.WxBinds.Add(new SysWxBind { FactoryId = 1, UserId = 1, ChanType = "wecom", WxOpenid = McpWxBindService.HashSession(platform), Status = 1 });
        await db.SaveChangesAsync();
        var bind = new McpWxBindService(db, new McpRequestContext { CurrentFactoryId = 1 }, config, http, NullLogger<McpWxBindService>.Instance);
        Assert.Null(await bind.ResolveQueryUserAsync(null, "wecom", platform));
        Assert.NotNull((await McpDataAccess.ResolveAsync(bind, config, "wecom", platform)).Hint);
        var count = await db.WxBinds.CountAsync(); await bind.BindAsync("13800000001", "wecom", platform);
        Assert.Equal(count, await db.WxBinds.CountAsync());
        http.HttpContext.Request.Headers["X-Mcp-Identity-Key"] = proof;
        // 模型sessionId和channel不能改掉可信请求头的本人。
        Assert.Equal(1, (await bind.ResolveQueryUserAsync(null, "coze", "attacker"))!.Id);
        var pending = tickets.Create(1, 1, "甲", "A");
        var lookup = tickets.Create(1, 1, "甲", "A"); var token = tickets.Redeem(lookup, 1, tickets.Approve(lookup, 1, 1)).Token;
        var result = JsonDocument.Parse(await bind.UnbindQueryAsync(token)); Assert.Equal(0, result.RootElement.GetProperty("code").GetInt32());
        await Assert.ThrowsAsync<BusinessException>(() => query.ResolveAsync(1, token));
        Assert.Throws<BusinessException>(() => tickets.Read(pending));
        Assert.Null(await bind.ResolveQueryUserAsync(null, null, null));
    }

    [Fact]
    public async Task McpMiddleware_RejectsInvalidFactoryLicenseAndConflictingLabels()
    {
        await using var db = IdentityTestDatabase.Open(); var license = new License();
        using var services = new ServiceCollection().AddSingleton(db).AddSingleton<IMcpRequestContext>(new McpRequestContext())
            .AddSingleton<ILicenseTierService>(license).BuildServiceProvider();
        async Task<DefaultHttpContext> Call(string factory, string? secondLabel = null)
        {
            var ctx = new DefaultHttpContext { RequestServices = services }; ctx.Request.Path = "/mcp";
            ctx.Request.Headers.Authorization = "Bearer fixture-token"; ctx.Response.Body = new MemoryStream();
            if (secondLabel != null) { ctx.Request.Headers["factoryid"] = "2"; ctx.Request.Headers["X-Factory-Id"] = secondLabel; }
            await new McpTokenMiddleware(_ => Task.CompletedTask, Config(("Mcp:Token", "fixture-token"),
                ("Mcp:FactoryId", factory)), NullLogger<McpTokenMiddleware>.Instance).InvokeAsync(ctx);
            return ctx;
        }
        Assert.Equal(401, (await Call("99")).Response.StatusCode);
        Assert.Equal(403, (await Call("2", "1")).Response.StatusCode);
        await Assert.ThrowsAsync<BusinessException>(() => Call("1"));
        Assert.Equal(1, license.Factory);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void QueryAndReportExpiryAndRedaction()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions()); var clock = new Clock();
        var tickets = new ReportIdentityTickets(cache, clock, "mcp-query-identity");
        var pending = tickets.Create(1, 1, "甲", "A"); clock.Now += TimeSpan.FromMinutes(11);
        Assert.Throws<BusinessException>(() => tickets.Read(pending));
        pending = tickets.Create(1, 1, "甲", "A"); var code = tickets.Approve(pending, 1, 1);
        clock.Now += TimeSpan.FromMinutes(6); Assert.Throws<BusinessException>(() => tickets.Redeem(pending, 1, code));
        pending = tickets.Create(1, 1, "甲", "A"); var token = tickets.Redeem(pending, 1, tickets.Approve(pending, 1, 1)).Token;
        clock.Now += TimeSpan.FromMinutes(31); Assert.Throws<BusinessException>(() => tickets.Resolve(token, 1));
        var redacted = LogRedactor.RedactText("{\"queryAccessToken\":\"query-secret\",\"X-Mcp-Identity-Key\":\"proxy-secret\"}", 2000);
        Assert.DoesNotContain("query-secret", redacted); Assert.DoesNotContain("proxy-secret", redacted);
    }

    [Fact]
    public async Task HttpBoundary_RealJwtCurrentAccountAndTwoFactoryOptions()
    {
        await using var db = IdentityTestDatabase.Open();
        db.Users.Add(new SysUser { Id = 4, FactoryId = 2, Account = "other-admin", Name = "乙", Role = 1, Password = "unused" });
        await db.SaveChangesAsync();
        var ai = DispatchProxy.Create<IAiAssistantService, McpDataAccessTests.ServiceProxy>();
        ((McpDataAccessTests.ServiceProxy)(object)ai).Call = (m, _) => m.Name == "GetStatus"
            ? ApiResult<AiStatusDto>.Ok(new AiStatusDto { Enabled = true }) : throw new InvalidOperationException("意外模型调用");
        var jwt = new JwtOptions { Secret = "tenant-isolation-426-test-signing-key-32-characters", Issuer = "fixture", Audience = "fixture" };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders(); builder.Services.AddSingleton(db); builder.Services.AddSingleton(ai);
        builder.Services.AddScoped<IMcpKeyService, McpKeyService>();
        builder.Services.AddControllers().AddApplicationPart(typeof(AiAssistantController).Assembly);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidIssuer = jwt.Issuer,
                ValidateAudience = true, ValidAudience = jwt.Audience, ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)), ValidateLifetime = true };
        });
        builder.Services.AddAuthorization();
        await using var app = builder.Build(); app.Urls.Add("http://127.0.0.1:0");
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        try
        {
            var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(url) };
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ai-assistant/status")).StatusCode);
            foreach (var (user, factory) in new[] { (1L, 1L), (4L, 2L) })
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtHelper.CreateToken(jwt, user, factory, 1, "fixture"));
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ai-assistant/status")).StatusCode);
                using var options = JsonDocument.Parse(await client.GetStringAsync("/api/McpKey/factories"));
                var list = options.RootElement.GetProperty("data"); Assert.Equal(1, list.GetArrayLength());
                Assert.Equal(factory, list[0].GetProperty("id").GetInt64());
            }
            var token = JwtHelper.CreateToken(jwt, 1, 1, 1, "fixture");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            db.Users.Single(u => u.Id == 1).Role = 2; await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ai-assistant/status")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/McpKey/factories")).StatusCode);
            db.Users.Single(u => u.Id == 1).Status = 0; await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ai-assistant/status")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token + "invalid");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ai-assistant/status")).StatusCode);
            var expired = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(jwt.Issuer, jwt.Audience,
                new[] { new System.Security.Claims.Claim(JwtHelper.ClaimUserId, "4"), new System.Security.Claims.Claim(JwtHelper.ClaimFactoryId, "2") },
                DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1), new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)), SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(expired));
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ai-assistant/status")).StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    private sealed class DisableDuringModel(AppDbContext db) : IDeepSeekClient
    {
        public async Task<DeepSeekChatResult> ChatAsync(IReadOnlyList<DeepSeekChatMessage> messages,
            IReadOnlyList<DeepSeekToolDef>? tools, CancellationToken ct)
        {
            db.Users.Single(u => u.Id == 1).Status = 0; await db.SaveChangesAsync(ct);
            return new DeepSeekChatResult { Content = "旧身份的答案" };
        }
        public Task<DeepSeekChatResult> ConnectionTestAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public async Task RevokedDuringModel_RejectsReturnedAnswerAndReleasesBusy()
    {
        await using var db = IdentityTestDatabase.Open();
        var options = Options.Create(new AiOptions { Enabled = true, ApiKey = "test", Model = "test" });
        var store = new AiSessionStore(options);
        var session = store.Create(1, 1, "progress"); session.Messages.Add(new AiSessionMessage { Role = "system", Content = "test" });
        var dispatcher = new AiToolDispatcher(db, null!, null!, null!, null!, null!, null!, options, NullLogger<AiToolDispatcher>.Instance);
        var ai = new AiAssistantService(options, new DisableDuringModel(db), store, dispatcher, null!, NullLogger<AiAssistantService>.Instance);
        var error = await Assert.ThrowsAsync<BusinessException>(() => ai.SendMessageAsync(
            new AiMessageRequest { Mode = "progress", ConversationId = session.Id, Message = "查工单" }, 1, 1, default));
        Assert.Equal(401, error.Code); Assert.False(session.Busy);
    }

    [Fact]
    public async Task SameOrderNumbersRemainInOwnFactory_ForeignIdRejected()
    {
        await using var db = IdentityTestDatabase.Open();
        db.Products.AddRange(new BaseProduct { Id = 1, FactoryId = 1, Code = "same", Name = "A厂产品" },
            new BaseProduct { Id = 2, FactoryId = 2, Code = "same", Name = "B厂秘密产品" });
        db.WorkOrders.AddRange(new ProdWorkOrder { Id = 1, FactoryId = 1, ProductId = 1, OrderNo = "same-no", Qty = 10 },
            new ProdWorkOrder { Id = 2, FactoryId = 2, ProductId = 2, OrderNo = "same-no", Qty = 20 });
        await db.SaveChangesAsync();
        var service = new WorkOrderService(db);
        await Assert.ThrowsAsync<BusinessException>(() => service.GetAsync(2, 1));
        var own = await service.GetByOrderNoAsync("same-no", 1, null, null);
        Assert.Equal("A厂产品", own.Data!.ProductName); Assert.Equal(1, own.Data.Id);
        var other = await service.GetByOrderNoAsync("same-no", 2, null, null);
        Assert.Equal("B厂秘密产品", other.Data!.ProductName); Assert.Equal(2, other.Data.Id);
    }

    [Theory]
    [InlineData(1, false, true)] [InlineData(1, true, false)] [InlineData(0, false, false)]
    public async Task DatabaseKeys_ExpiryAndRevocationAreChecked(byte status, bool expired, bool allowed)
    {
        await using var db = IdentityTestDatabase.Open(); var current = new McpRequestContext();
        db.McpKeys.Add(new SysMcpKey { FactoryId = 2, KeyAlias = "test", ApiKeyHash = PasswordHelper.Hash("fixture-token"),
            Status = status, ExpireAt = expired ? DateTime.Now.AddMinutes(-1) : DateTime.Now.AddMinutes(10) });
        await db.SaveChangesAsync();
        using var services = new ServiceCollection().AddSingleton(db).AddSingleton<IMcpRequestContext>(current)
            .AddSingleton<ILicenseTierService>(new License()).BuildServiceProvider();
        var ctx = new DefaultHttpContext { RequestServices = services }; ctx.Request.Path = "/mcp";
        ctx.Request.Headers.Authorization = "Bearer fixture-token"; ctx.Response.Body = new MemoryStream();
        var called = false;
        await new McpTokenMiddleware(_ => { called = true; return Task.CompletedTask; }, Config(),
            NullLogger<McpTokenMiddleware>.Instance).InvokeAsync(ctx);
        Assert.Equal(allowed, called); Assert.Equal(allowed ? 200 : 401, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task PairingPage_UsesPairFactoryNotConfiguration()
    {
        var pairing = DispatchProxy.Create<IMcpAuthService, McpDataAccessTests.ServiceProxy>(); var license = new License();
        var factory = 2L;
        ((McpDataAccessTests.ServiceProxy)(object)pairing).Call = (m, _) => m.Name switch
        {
            "ResolveFactoryIdByCodeAsync" => Task.FromResult(factory),
            "PeekCodeAsync" => Task.CompletedTask,
            _ => throw new InvalidOperationException(m.Name)
        };
        var controller = new McpAuthController(pairing, null!, Config(("Mcp:FactoryId", "1")), license);
        Assert.Contains("<form", (await controller.Index("fixture-code")).Content);
        Assert.Equal(2, license.Factory);
        factory = 1;
        Assert.Contains("授权失败", (await controller.Index("fixture-code")).Content);
        Assert.Equal(1, license.Factory);
    }

    [Fact]
    public async Task DatabaseFailure_DoesNotBecomeSuccessfulIdentity()
    {
        var db = IdentityTestDatabase.Open(); await db.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => CurrentAccountAccess.RequireAsync(db, 1, 1));
    }
}
