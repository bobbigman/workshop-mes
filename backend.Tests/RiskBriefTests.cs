using System.Net.Http.Json;
using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Mcp;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using Xunit;
using Xunit.Abstractions;

namespace WorkshopMes.Tests;

public class RiskBriefTests(ITestOutputHelper output)
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
    private static readonly DateTime Today = new(2026, 10, 20); // 周二
    private static readonly TimeProvider FixedClock = new Clock(new DateTimeOffset(2026, 10, 20, 4, 0, 0, TimeSpan.Zero));
    private static IConfiguration Config(bool demo = false) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Mcp:DemoSkipAuth"] = demo.ToString(), ["Ai:BusinessTimeZone"] = "Asia/Shanghai" }).Build();

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public AppDbContext Db { get; private set; } = null!;
        public async Task Init()
        {
            await connection.OpenAsync();
            Db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await Db.Database.EnsureCreatedAsync();
            Db.Products.AddRange(new BaseProduct { Id = 1, FactoryId = 1, Code = "P1", Name = "普通产品" },
                new BaseProduct { Id = 2, FactoryId = 1, Code = "P2", Name = "重点产品", IsRiskFocus = true },
                new BaseProduct { Id = 3, FactoryId = 2, Code = "P1", Name = "外厂机密", IsRiskFocus = true });
            Db.Operations.AddRange(new BaseOperation { Id = 1, FactoryId = 1, Code = "O1", Name = "下料" },
                new BaseOperation { Id = 2, FactoryId = 1, Code = "O2", Name = "装配" });
            await Db.SaveChangesAsync();
        }
        public void Order(long id, int? dueDays, int age = 0, byte status = 1, long productId = 1, long factoryId = 1, bool ops = true)
        {
            Db.WorkOrders.Add(new ProdWorkOrder { Id = id, FactoryId = factoryId, ProductId = productId,
                OrderNo = $"GD{id}", Qty = 100, Status = status, DueDate = dueDays.HasValue ? Today.AddDays(dueDays.Value) : null,
                CreatedAt = Today.AddDays(-age) });
            if (ops)
                Db.WorkOrderOperations.AddRange(new ProdWorkOrderOperation { WorkOrderId = id, OperationId = 1, Seq = 1, PlanQty = 100 },
                    new ProdWorkOrderOperation { WorkOrderId = id, OperationId = 2, Seq = 2, PlanQty = 100 });
        }
        public void Report(long id, long op, int good, int daysAgo, byte status = 1, long factoryId = 1)
            => Db.Reports.Add(new ProdReport { FactoryId = factoryId, OrderId = id, OperationId = op,
                GoodQty = good, ReviewStatus = status, ReportTime = Today.AddDays(-daysAgo) });
        public RiskBriefService Service(TimeProvider? clock = null) => new(Db, Config(), clock ?? FixedClock);
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    [Fact]
    public async Task Today_Week_Boundaries_ExcludeClosedAndOtherFactories()
    {
        await using var f = new Fixture(); await f.Init();
        f.Order(1, -1); f.Order(2, 0); f.Order(3, 3); f.Order(4, 4); f.Order(5, 5); f.Order(6, 6);
        f.Order(7, -4, status: 2); f.Order(8, -4, status: 3);
        f.Order(9, -5, factoryId: 2, productId: 3);
        // 损坏的跨厂产品引用也不能泄露外厂产品。
        f.Order(10, -1, productId: 3);
        await f.Db.SaveChangesAsync();
        var today = await f.Service().QueryAsync(1, "today", 10);
        Assert.Equal(new long[] { 1, 2, 3 }, today.List.Select(r => r.Id));
        Assert.Equal("2026-10-23", today.DueThroughDate);
        var week = await f.Service().QueryAsync(1, "本周", 10);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, week.List.Select(r => r.Id));
        Assert.Equal("2026-10-25", week.DueThroughDate);
        Assert.Equal(1, today.List[0].OverdueDays);
        Assert.Equal(0, today.List[1].DaysToDue);
    }

    [Fact]
    public async Task FullSetRanking_FocusBeforeOverdue_ThenTruncate()
    {
        await using var f = new Fixture(); await f.Init();
        for (int i = 1; i <= 25; i++) f.Order(i, -i);
        f.Order(100, 2, productId: 2);
        await f.Db.SaveChangesAsync();
        var r = await f.Service().QueryAsync(1, topN: 2);
        Assert.Equal(26, r.Total); Assert.True(r.Truncated);
        Assert.Equal(new long[] { 100, 25 }, r.List.Select(x => x.Id));
        Assert.Contains("你的重点产品", r.Summary);
        Assert.Contains("GD100", r.FallbackText); Assert.Contains("建议", r.FallbackText);
    }

    [Fact]
    public async Task Stagnation_ReviewTracks_MinProgress_CompletedOpsAndFutureReports()
    {
        await using var f = new Fixture(); await f.Init();
        f.Order(1, null, age: 10);
        f.Report(1, 1, 100, 9); // 已满工序不能判滞留
        f.Report(1, 2, 10, 5, status: 0); // 待复核有效
        f.Report(1, 2, 20, 4); // 通过有效，最后4天
        f.Report(1, 2, 60, 0, status: 2); // 退回既不加进度也不刷新滞留
        f.Report(1, 2, 70, -1); // 未来记录不纳入当前快照
        f.Report(1, 2, 100, 0, factoryId: 2); // 跨厂脏数据不计入
        f.Db.Operations.Add(new BaseOperation { Id = 3, FactoryId = 2, Code = "FOREIGN", Name = "外厂秘密工序" });
        f.Db.WorkOrderOperations.Add(new ProdWorkOrderOperation { WorkOrderId = 1, OperationId = 3, Seq = 3, PlanQty = 100 });
        f.Order(2, null, age: 3); // 恰好3天不滞留
        f.Order(3, null, age: 4); // 无报工用创建日
        f.Order(4, null, age: 10);
        f.Report(4, 1, 100, 9); f.Report(4, 2, 100, 9); // 全工序完成
        await f.Db.SaveChangesAsync();
        var r = await f.Service().QueryAsync(1, topN: 10);
        Assert.Equal(new long[] { 1, 3 }, r.List.Select(x => x.Id));
        Assert.Equal(30, r.List[0].DoneQty); Assert.Equal(70, r.List[0].RemainQty);
        Assert.Equal(30, r.List[0].ProgressPercent);
        var op = Assert.Single(r.List[0].StagnantOperations);
        Assert.Equal("装配", op.OperationName); Assert.Equal(4, op.DaysWithoutReport);
        Assert.Equal(2, r.List[1].StagnantOperations.Count);
    }

    [Fact]
    public async Task NoRisk_EmptyData_HasSafeSummary_AndNoPlanDoesNotInventStagnation()
    {
        await using var f = new Fixture(); await f.Init();
        f.Order(1, null, age: 10, ops: false);
        await f.Db.SaveChangesAsync();
        var r = await f.Service().QueryAsync(1);
        Assert.Empty(r.List); Assert.Equal(0, r.Total); Assert.False(r.Truncated);
        Assert.Equal("今日无超期、临期或滞留风险工单", r.Summary);
        Assert.Equal(r.Summary, r.FallbackText);
        Assert.Contains("不含不良超标", r.ScopeNote);
    }

    [Theory]
    [InlineData("2026-10-25T16:01:00Z", "2026-10-26", "2026-11-01")]
    [InlineData("2026-10-25T15:59:00Z", "2026-10-25", "2026-10-25")]
    public async Task WeekRolloverUsesBusinessTimezone(string utc, string date, string through)
    {
        await using var f = new Fixture(); await f.Init();
        var r = await f.Service(new Clock(DateTimeOffset.Parse(utc))).QueryAsync(1, "week");
        Assert.Equal(date, r.AsOfDate); Assert.Equal(through, r.DueThroughDate);
    }

    [Theory]
    [InlineData("month", 3)] [InlineData("", 3)] [InlineData("today", 0)] [InlineData("week", 11)]
    public async Task InvalidParametersRejected(string period, int topN)
    {
        await using var f = new Fixture(); await f.Init();
        await Assert.ThrowsAsync<BusinessException>(() => f.Service().QueryAsync(1, period, topN));
    }

    [Fact]
    public async Task FocusProduct_Persists_CompatUpdatePreserves_CanClear_AndRejectsOtherFactory()
    {
        await using var f = new Fixture(); await f.Init();
        var s = new ProductService(f.Db);
        await s.UpdateAsync(1, new() { Code = "P1", Name = "改名", IsRiskFocus = true }, 1);
        f.Db.ChangeTracker.Clear();
        Assert.True((await s.GetAsync(1, 1)).Data!.IsRiskFocus);
        await s.UpdateAsync(1, new() { Code = "P1", Name = "旧客户端" }, 1);
        Assert.True((await s.QueryAsync(new(), 1)).Data!.List.Single(p => p.Id == 1).IsRiskFocus);
        await Assert.ThrowsAsync<BusinessException>(() => s.UpdateAsync(3, new() { Code = "P1", Name = "外厂", IsRiskFocus = false }, 1));
        await s.UpdateAsync(1, new() { Code = "P1", Name = "普通", IsRiskFocus = false }, 1);
        Assert.False((await s.GetAsync(1, 1)).Data!.IsRiskFocus);
        await s.CreateAsync(new() { Code = "COPY", Name = "复制", CopyFromId = 2 }, 1);
        Assert.False((await f.Db.Products.SingleAsync(p => p.Code == "COPY")).IsRiskFocus);
    }

    private sealed class Binding : IMcpWxBindService
    {
        public SysUser? User { get; set; } = new() { Id = 1, FactoryId = 1, Role = 1, Status = 1, Name = "管理员" };
        public Task<SysUser?> ResolveBoundUserAsync(string? c, string? s, CancellationToken ct = default) => Task.FromResult(User);
        public Task<string> BindAsync(string p, string? c, string? s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> UnbindAsync(string? c, string? s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> RequireBoundOrHintAsync(string? c, string? s, string t, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class SpyService : IRiskBriefService
    {
        public long? Factory;
        public Task<RiskBriefDto> QueryAsync(long factoryId, string? period = null, int topN = 3, CancellationToken ct = default)
        { Factory = factoryId; return Task.FromResult(new RiskBriefDto()); }
    }

    [Theory]
    [InlineData(2, 1)] [InlineData(3, 1)] [InlineData(4, 1)] [InlineData(1, 0)]
    public async Task DeniedBeforeQuery_EvenWithFinanceToken(byte role, byte status)
    {
        var binding = new Binding(); binding.User!.Role = role; binding.User.Status = status;
        var spy = new SpyService();
        var tool = new RiskBriefMcpTools(spy, new McpRequestContext { CurrentFactoryId = 1 }, binding, Config());
        using var json = JsonDocument.Parse(await tool.QueryRiskBrief(authToken: "finance-does-not-grant-access"));
        Assert.Equal(1, json.RootElement.GetProperty("code").GetInt32()); Assert.Null(spy.Factory);
    }

    [Fact]
    public async Task UnboundMissingFactoryAndCrossFactoryDenied_DemoStillNeedsFactory()
    {
        var binding = new Binding { User = null }; var spy = new SpyService();
        var ctx = new McpRequestContext { CurrentFactoryId = 1 };
        using var json = JsonDocument.Parse(await new RiskBriefMcpTools(spy, ctx, binding, Config()).QueryRiskBrief());
        Assert.Equal(1, json.RootElement.GetProperty("code").GetInt32()); Assert.Null(spy.Factory);
        ctx.CurrentFactoryId = null;
        await Assert.ThrowsAsync<BusinessException>(() => new RiskBriefMcpTools(spy, ctx, binding, Config(true)).QueryRiskBrief());
        binding.User = new() { FactoryId = 2, Role = 1, Status = 1 };
        ctx.CurrentFactoryId = 1;
        await Assert.ThrowsAsync<BusinessException>(() => new RiskBriefMcpTools(spy, ctx, binding, Config()).QueryRiskBrief());
        Assert.Null(spy.Factory);
        await new RiskBriefMcpTools(spy, ctx, binding, Config(true)).QueryRiskBrief();
        Assert.Equal(1, spy.Factory);
    }

    [Fact]
    public void SqlServerMigrationIsDiscoverableAndIdempotent()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true").Options);
        Assert.Contains("202610200001_RiskFocusProduct", db.Database.GetMigrations());
        var script = db.GetService<IMigrator>().GenerateScript();
        Assert.Contains("COL_LENGTH(N'dbo.base_product', N'is_risk_focus') IS NULL", script);
        Assert.Contains("DEFAULT 0 WITH VALUES", script);
    }

    [Fact]
    public async Task RealMcpHttp_ToolsListAndRiskCall()
    {
        await using var f = new Fixture(); await f.Init();
        f.Order(1, -2, productId: 2); await f.Db.SaveChangesAsync();
        var builder = WebApplication.CreateBuilder();
        var binding = new Binding();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IRiskBriefService>(f.Service());
        builder.Services.AddSingleton<IMcpRequestContext>(new McpRequestContext { CurrentFactoryId = 1 });
        builder.Services.AddSingleton<IMcpWxBindService>(binding);
        builder.Services.AddSingleton(Config());
        builder.Services.AddMcpServer().WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
            .WithToolsFromAssembly(typeof(RiskBriefMcpTools).Assembly);
        await using var app = builder.Build(); app.MapMcp("/mcp"); await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var http = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        http.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        async Task<JsonElement> Rpc(object request)
        {
            using var response = await http.PostAsJsonAsync("/mcp", request);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, body);
            var jsonText = body.StartsWith("{") ? body : body.Split('\n').First(x => x.StartsWith("data: "))[6..];
            using var json = JsonDocument.Parse(jsonText);
            Assert.False(json.RootElement.TryGetProperty("error", out _), jsonText);
            return json.RootElement.GetProperty("result").Clone();
        }
        await Rpc(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new
            { protocolVersion = "2025-03-26", capabilities = new { }, clientInfo = new { name = "risk-brief-local-check", version = "1" } } });
        var tools = await Rpc(new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } });
        var names = tools.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToArray();
        output.WriteLine($"Local tools/list count={names.Length}: {string.Join(", ", names)}");
        Assert.Contains("query_risk_brief", names); Assert.Contains("query_work_order_progress", names);
        var riskSchema = tools.GetProperty("tools").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "query_risk_brief");
        Assert.False(riskSchema.GetProperty("inputSchema").GetProperty("properties").TryGetProperty("cancellationToken", out _));
        var result = await Rpc(new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new
            { name = "query_risk_brief", arguments = new { period = "week", topN = 3 } } });
        Assert.False(result.TryGetProperty("isError", out var err) && err.GetBoolean(), result.ToString());
        using var payload = JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!);
        Assert.Equal(0, payload.RootElement.GetProperty("code").GetInt32());
        Assert.True(payload.RootElement.GetProperty("data").GetProperty("list")[0].GetProperty("isRiskFocus").GetBoolean());
        binding.User!.Role = 2; // 下一次请求即时重读角色
        var denied = await Rpc(new { jsonrpc = "2.0", id = 4, method = "tools/call", @params = new
            { name = "query_risk_brief", arguments = new { period = "today" } } });
        using var deniedPayload = JsonDocument.Parse(denied.GetProperty("content")[0].GetProperty("text").GetString()!);
        Assert.Equal(1, deniedPayload.RootElement.GetProperty("code").GetInt32());
        Assert.False(deniedPayload.RootElement.TryGetProperty("data", out _));
        await app.StopAsync();
    }
}
