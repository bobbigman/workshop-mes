using System.Reflection;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Services;
using ahu.MicrosoftMes.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace WorkshopMes.Tests;

public class WechatRiskBriefPushTests
{
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
    }
    private sealed class License : ILicenseTierService
    {
        public bool Allowed = true;
        public Task<bool> CanUseAsync(long id, LicenseFeature feature) => Task.FromResult(Allowed);
        public Task<LicenseTier> GetTierAsync(long id) => throw new NotSupportedException();
        public Task EnsureFeatureAsync(long id, LicenseFeature feature) => Task.CompletedTask;
    }
    private sealed class Brief : IRiskBriefService
    {
        public int Calls;
        public bool Fail;
        public RiskBriefDto Value = new() { AsOfDate = "2026-10-20", Summary = "今日无风险", FallbackText = "备用文本" };
        public Task<RiskBriefDto> QueryAsync(long factoryId, string? period = null, int topN = 3, CancellationToken ct = default)
        {
            Calls++; Assert.Equal(1, factoryId); Assert.Equal("today", period); Assert.Equal(3, topN);
            if (Fail) throw new InvalidOperationException("query failed");
            return Task.FromResult(Value);
        }
    }
    private sealed class Model : IDeepSeekClient
    {
        public int Calls;
        public bool Fail;
        public string? Text = "你的重点产品GD1滞留，请优先催办。";
        public Task<DeepSeekChatResult> ChatAsync(IReadOnlyList<DeepSeekChatMessage> messages, IReadOnlyList<DeepSeekToolDef>? tools, CancellationToken ct)
        {
            Calls++;
            if (Fail) throw new TimeoutException("model timeout");
            return Task.FromResult(new DeepSeekChatResult { Content = Text });
        }
        public Task<DeepSeekChatResult> ConnectionTestAsync(CancellationToken ct) => throw new NotSupportedException();
    }
    // 仅替代真实SQL发送通道；不能据此声称已验证SQL锁/额度账本。
    private sealed class Sender(AppDbContext db, License license) : WechatMessageSender(db, null!, license, NullLogger<WechatMessageSender>.Instance)
    {
        public string Outcome = "success";
        public string? Content;
        public DateTime? Date;
        public string? Key;
        public override async Task<WechatSendResult> SendAsync(long factoryId, string alertKey, string content,
            string? recipients = null, long? deliveryId = null, Guid? leaseId = null, bool test = false,
            string? channel = null, Func<Task<string>>? contentFactory = null, DateTime? batchDate = null)
        {
            Date = batchDate; Key = alertKey;
            if (Outcome != "success") return new(Outcome);
            Content = contentFactory == null ? content : await contentFactory();
            return new("success");
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public AppDbContext Db = null!;
        public License License = new();
        public Brief Brief = new();
        public Model Model = new();
        public Sender Sender = null!;
        public WechatAlertService Service = null!;
        public async Task Init()
        {
            await connection.OpenAsync();
            Db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await Db.Database.EnsureCreatedAsync();
            Db.WechatAlertSettings.Add(new() { FactoryId = 1, Enabled = true, WebhookKey = "test-key", DailyLimit = 20 });
            await Db.SaveChangesAsync();
            Sender = new(Db, License);
            Service = new(Db, null!, null!, License, Sender, Model,
                Options.Create(new AiOptions { ApiKey = "test", Model = "test", BusinessTimeZone = "Asia/Shanghai" }),
                NullLogger<WechatAlertService>.Instance, Brief, new Clock());
        }
        public Task<WechatRiskBriefPushResultDto> Push() => (Task<WechatRiskBriefPushResultDto>)typeof(WechatAlertService)
            .GetMethod("PushRiskBriefForFactoryAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(Service, new object[] { 1L })!;
        public void Risk() => Brief.Value.List.Add(new() { OrderNo = "GD1", ProductName = "重点A", IsRiskFocus = true,
            ProgressPercent = 20, Reasons = new() { "滞留4天" }, Suggestion = "催办下料" });
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
    [Fact]
    public async Task EmptyDoesNotCallModelAndUsesBusinessDate()
    {
        await using var f = new Fixture(); await f.Init();
        Assert.Equal("sent", (await f.Push()).Status);
        Assert.Equal(0, f.Model.Calls); Assert.Contains("备用文本", f.Sender.Content);
        Assert.Equal(new DateTime(2026, 10, 20), f.Sender.Date); Assert.Equal("risk-brief:2026-10-20", f.Sender.Key);
    }
    [Fact]
    public async Task RiskContainsFocusProgressReasonsAndSuggestion()
    {
        await using var f = new Fixture(); await f.Init(); f.Risk();
        var result = await f.Push();
        Assert.Equal("sent", result.Status); Assert.Equal(1, result.FocusCount);
        foreach (var value in new[] { "GD1", "你的重点产品", "20%", "滞留4天", "催办下料" }) Assert.Contains(value, f.Sender.Content);
    }
    [Theory]
    [InlineData("dedup")][InlineData("limited")][InlineData("unknown")][InlineData("busy")]
    public async Task SenderSkipDoesNotQueryOrCallModel(string outcome)
    {
        await using var f = new Fixture(); await f.Init(); f.Sender.Outcome = outcome;
        Assert.Equal("skipped_" + outcome, (await f.Push()).Status);
        Assert.Equal(0, f.Brief.Calls); Assert.Equal(0, f.Model.Calls);
    }
    [Theory]
    [InlineData("empty")][InlineData("long")][InlineData("missing-focus")][InlineData("timeout")]
    public async Task InvalidModelFallsBack(string kind)
    {
        await using var f = new Fixture(); await f.Init(); f.Risk();
        f.Model.Text = kind == "empty" ? " " : kind == "long" ? new string('字', 121) : "请催办";
        f.Model.Fail = kind == "timeout";
        Assert.Equal("sent", (await f.Push()).Status); Assert.StartsWith("备用文本", f.Sender.Content);
    }
    [Fact]
    public async Task QueryFailurePropagates()
    {
        await using var f = new Fixture(); await f.Init(); f.Brief.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Push()); Assert.Null(f.Sender.Content);
    }
    [Fact]
    public async Task CrossDateQueryFailsBeforeModel()
    {
        await using var f = new Fixture(); await f.Init(); f.Brief.Value.AsOfDate = "2026-10-21";
        await Assert.ThrowsAsync<BusinessException>(() => f.Push()); Assert.Equal(0, f.Model.Calls);
    }
    [Theory]
    [InlineData("tier")][InlineData("disabled")][InlineData("unconfigured")]
    public async Task GuardsSkipBeforeQuery(string guard)
    {
        await using var f = new Fixture(); await f.Init();
        var setting = await f.Db.WechatAlertSettings.SingleAsync();
        if (guard == "tier") f.License.Allowed = false;
        if (guard == "disabled") setting.Enabled = false;
        if (guard == "unconfigured") setting.WebhookKey = null;
        await f.Db.SaveChangesAsync();
        Assert.Equal("skipped_" + guard, (await f.Push()).Status); Assert.Equal(0, f.Brief.Calls);
    }
    [Theory]
    [InlineData("success", 0, "sent")][InlineData("limited", 1, "skipped_limited")]
    public async Task CronIncludesBriefAndReportsFailures(string outcome, int code, string status)
    {
        await using var f = new Fixture(); await f.Init(); f.Sender.Outcome = outcome;
        var result = await f.Service.PushDueAlertCronAsync(); Assert.Equal(code, result.Code);
        var data = System.Text.Json.JsonSerializer.SerializeToElement(result.Data);
        Assert.Single(data.GetProperty("list").EnumerateArray());
        Assert.Equal(status, data.GetProperty("riskBriefList")[0].GetProperty("Status").GetString());
    }
    [Fact]
    public async Task EmptyCronPreservesOldFieldsAndAddsBriefFields()
    {
        await using var f = new Fixture(); await f.Init();
        (await f.Db.WechatAlertSettings.SingleAsync()).Enabled = false; await f.Db.SaveChangesAsync();
        var result = await f.Service.PushDueAlertCronAsync(); Assert.Equal(0, result.Code);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Data);
        foreach (var field in new[] { "list", "sent", "skipped", "failed", "riskBriefList", "riskBriefSent", "riskBriefSkipped", "riskBriefFailed" }) Assert.Contains("\"" + field + "\"", json);
    }
}
