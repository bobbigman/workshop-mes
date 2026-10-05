using System.Net;
using System.Text;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Controllers;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using ahu.MicrosoftMes.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WorkshopMes.Tests;

public class WechatEventContractTests
{
    [Theory]
    [InlineData(0, 1, "order_started")]
    [InlineData(0, 2, "order_completed")]
    [InlineData(1, 2, "order_completed")]
    [InlineData(2, 1, null)]
    [InlineData(2, 2, null)]
    [InlineData(3, 1, null)]
    public void FinalStateOnly(byte before, byte after, string? expected) => Assert.Equal(expected, WechatEventService.StatusEvent(before, after));

    [Theory]
    [InlineData(89, 11, true)]
    [InlineData(90, 10, false)]
    [InlineData(0, 0, false)]
    public void DefectRateStrictlyGreater(int good, int bad, bool expected) => Assert.Equal(expected,
        WechatEventService.Matches(new() { ConditionType = "defect_rate", Threshold = 10 }, good, bad));

    [Fact]
    public void RejectInvalidRuleAndRecipients()
    {
        Assert.Throws<BusinessException>(() => WechatEventService.ValidateRule(new() { Name = "规则", EventType = "order_started", Template = "{工序}" }));
        Assert.Throws<BusinessException>(() => WechatEventService.ValidateRule(new() { Name = "规则", Template = "{未知}" }));
        Assert.Throws<BusinessException>(() => WechatEventService.ValidateRule(new() { Name = "规则", Template = "{工单号", ConditionType = "always" }));
        Assert.Throws<BusinessException>(() => WechatMessageSender.NormalizeUsers("@all"));
        Assert.Equal("Alice|Bob", WechatMessageSender.NormalizeUsers("Alice，Bob|alice"));
        Assert.Equal("abc-key", WechatMessageSender.NormalizeWebhookKey("https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=abc-key"));
        Assert.Equal("success", WechatMessageSender.ParseWebhookResponse("{\"errcode\":0}").Outcome);
    }

    [Theory]
    [InlineData("{\"errcode\":0}", "success", null)]
    [InlineData("{\"errcode\":0,\"invaliduser\":\"bob\"}", "partial", "Bob")]
    [InlineData("{\"errcode\":0,\"unlicenseduser\":\"alice|bob\"}", "failed", "Alice|Bob")]
    [InlineData("{\"errcode\":81013}", "failed", null)]
    [InlineData("{}", "unknown", null)]
    public void InterpretAcceptance(string raw, string outcome, string? failed)
    {
        var result = WechatMessageSender.ParseResponse(raw, "Alice|Bob");
        Assert.Equal(outcome, result.Outcome); Assert.Equal(failed, result.FailedUsers);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("Alice, Bob|alice", "Alice|Bob")]
    [InlineData("Alice,@all", "@all")]
    public void GroupMentionsAreSeparateFromSession(string? input, string expected) =>
        Assert.Equal(expected, WechatMessageSender.NormalizeGroupUsers(input));

    [Theory]
    [InlineData("@Alice")]
    [InlineData("@ALL")]
    public void GroupRejectsOtherAtTokens(string input) =>
        Assert.Throws<BusinessException>(() => WechatMessageSender.NormalizeGroupUsers(input));

    [Theory]
    [InlineData("http://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=abc")]
    [InlineData("https://wrong.example/cgi-bin/webhook/send?key=abc")]
    [InlineData("https://qyapi.weixin.qq.com/wrong?key=abc")]
    [InlineData("https://qyapi.weixin.qq.com/cgi-bin/webhook/send")]
    [InlineData("https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=")]
    public void RejectNonOfficialOrEmptyWebhook(string input) =>
        Assert.Throws<BusinessException>(() => WechatMessageSender.NormalizeWebhookKey(input));

    [Fact]
    public void MaskAndClearAreDifferentAndNoDefaultMentionInheritance()
    {
        Assert.Equal("original", WechatMessageSender.ResolveWebhookKey("********", "original"));
        Assert.Null(WechatMessageSender.ResolveWebhookKey("", "original"));
        var setting = new SysWechatAlertSetting { WebhookKey = "default", ToUser = "@all" };
        var rule = new SysWechatAlertRule { TargetChannel = "group" };
        Assert.True(WechatEventService.TryRecipients(rule, setting, out var users)); Assert.Equal("(group)", users);
        rule.TargetChannel = "session"; Assert.False(WechatEventService.TryRecipients(rule, setting, out _));
        Assert.Equal("group", new WechatRuleDto().TargetChannel);
    }

    [Theory]
    [InlineData("abnormal_device", 1, true)]
    [InlineData("abnormal_device", 2, false)]
    [InlineData("abnormal_material", 2, true)]
    [InlineData("abnormal_quality", 3, true)]
    [InlineData("abnormal_quality", 1, false)]
    [InlineData("always", 3, true)]
    public void AbnormalTypesMatchOnlyTheirRule(string condition, byte type, bool expected) =>
        Assert.Equal(expected, WechatEventService.MatchesAbnormal(condition, type));

    [Fact]
    public void WebhookContextAndCamelCaseRequestAreRedacted()
    {
        var safe = WechatMessageSender.RedactWebhookContext("request?key=secret-key response echoed secret-key", "secret-key");
        Assert.DoesNotContain("secret-key", safe);
        var body = LogRedactor.RedactJsonOrForm("{\"webhookKey\":\"secret-key\"}", 4096);
        Assert.DoesNotContain("secret-key", body.Text);
        var sql = System.Text.Json.JsonSerializer.Serialize(LogRedactor.RedactSqlParameters(
            [new() { Name = "@p0", Value = "secret-key" }], "UPDATE sys_wechat_alert_rule SET webhook_key=@p0", 4096));
        Assert.DoesNotContain("secret-key", sql);
    }

    [Fact]
    public void Utf8MessageDoesNotSplitCharacter() => Assert.InRange(Encoding.UTF8.GetByteCount(WechatMessageSender.FitText(new string('中', 1000))), 1, 2000);
}

[CollectionDefinition("wechat129", DisableParallelization = true)]
public class Wechat129Collection : ICollectionFixture<Wechat129Database> { }

public sealed class Wechat129Database : IAsyncLifetime
{
    public string Connection { get; }
    private bool created;
    private readonly string databaseName = "WorkshopMes_129_Test_" + Guid.NewGuid().ToString("N");
    public Wechat129Database()
    {
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("MES_TEST_DB") ??
            "Server=localhost;Database=WorkshopMes_Test;Trusted_Connection=True;TrustServerCertificate=True");
        builder.InitialCatalog = databaseName;
        Connection = builder.ConnectionString;
    }
    public AppDbContext Open() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Connection).Options);
    public async Task InitializeAsync()
    {
        await using var db = Open(); await db.Database.EnsureCreatedAsync(); created = true;
        // EnsureCreated 新库与存量升级脚本均须可用；重复升级不丢数据。
        await db.Database.ExecuteSqlRawAsync(WechatEventSchema.UpgradeSql);
        await db.Database.ExecuteSqlRawAsync(WechatEventSchema.GroupUpgradeSql);
        await db.Database.ExecuteSqlRawAsync(WechatEventSchema.WebhookOnlyUpgradeSql);
        await db.Database.ExecuteSqlRawAsync(WechatEventSchema.UpgradeSql);
        await db.Database.ExecuteSqlRawAsync(WechatEventSchema.GroupUpgradeSql);
        await db.Database.ExecuteSqlRawAsync(WechatEventSchema.WebhookOnlyUpgradeSql);
    }
    public async Task DisposeAsync()
    {
        await using var db = Open();
        var name = new SqlConnectionStringBuilder(Connection).InitialCatalog;
        if (created && name == databaseName && name.StartsWith("WorkshopMes_129_Test_", StringComparison.Ordinal)) await db.Database.EnsureDeletedAsync();
    }
}

[Collection("wechat129")]
public class WechatEventDatabaseTests(Wechat129Database fixture) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var db = fixture.Open();
        await db.Database.ExecuteSqlRawAsync(WechatEventSchema.GroupUpgradeSql);
        await db.Database.ExecuteSqlRawAsync(WechatEventSchema.WebhookOnlyUpgradeSql);
        await db.WechatAlertDeliveries.ExecuteDeleteAsync();
        await db.WechatSendAttempts.ExecuteDeleteAsync();
        await db.WechatAlertLogs.ExecuteDeleteAsync();
        await db.WechatAlertSettings.ExecuteUpdateAsync(x => x.SetProperty(v => v.Enabled, false));
    }
    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class FakeLicense : ILicenseTierService
    {
        public bool Allowed = true;
        public Task<LicenseTier> GetTierAsync(long id) => Task.FromResult(Allowed ? LicenseTier.Enterprise : LicenseTier.Trial);
        public Task<bool> CanUseAsync(long id, LicenseFeature feature) => Task.FromResult(Allowed);
        public Task EnsureFeatureAsync(long id, LicenseFeature feature) => Task.CompletedTask;
    }
    private sealed class FakeHttp : HttpMessageHandler, IHttpClientFactory
    {
        public string MessageResponse = "{\"errcode\":0}";
        public bool FailMessage;
        public bool DelayMessages;
        public readonly List<string> Recipients = [];
        public readonly List<string> Paths = [];
        public readonly List<string> ClientNames = [];
        public HttpClient CreateClient(string name)
        {
            ClientNames.Add(name);
            return new(this, false) { Timeout = TimeSpan.FromSeconds(15) };
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri!.AbsolutePath.EndsWith("gettoken"))
            {
                return new(HttpStatusCode.OK) { Content = new StringContent("{\"errcode\":0,\"access_token\":\"test-secret-token\"}") };
            }
            if (DelayMessages) await Task.Delay(100, ct);
            var content = await request.Content!.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(content);
            if (request.RequestUri.AbsolutePath.Contains("webhook/send"))
            {
                var mentioned = doc.RootElement.GetProperty("text").TryGetProperty("mentioned_list", out var list) && list.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? string.Join('|', list.EnumerateArray().Select(x => x.GetString()))
                    : "";
                Recipients.Add(string.IsNullOrEmpty(mentioned) ? "(group)" : mentioned);
            }
            else
                Recipients.Add(doc.RootElement.GetProperty("touser").GetString()!);
            if (FailMessage) throw new HttpRequestException("response lost");
            return new(HttpStatusCode.OK) { Content = new StringContent(MessageResponse) };
        }
    }
    private static (WechatEventService events, WechatMessageSender sender) Services(AppDbContext db, FakeLicense? license = null, FakeHttp? http = null)
    {
        license ??= new(); http ??= new();
        var sender = new WechatMessageSender(db, http, license, NullLogger<WechatMessageSender>.Instance);
        return (new(db, license, sender, NullLogger<WechatEventService>.Instance), sender);
    }
    private static async Task<(ProdWorkOrder order, SysUser user, BaseOperation op)> SeedAsync(AppDbContext db, WechatEventService svc, string users = "Alice|Bob", int limit = 20)
    {
        var now = DateTime.Now;
        var factory = new SysFactory { FactoryCode = "TEST129-" + Guid.NewGuid().ToString("N"), FactoryName = "测试", LicenseTier = "enterprise", CreatedAt = now };
        db.Factories.Add(factory); await db.SaveChangesAsync();
        var user = new SysUser { FactoryId = factory.Id, Account = "admin", Name = "管理员", Password = "x", Role = 1, Status = 1, CreatedAt = now };
        var unit = new BaseUnit { FactoryId = factory.Id, Name = "件", CreatedAt = now };
        var op = new BaseOperation { FactoryId = factory.Id, Code = "CUT", Name = "裁切", CreatedAt = now };
        db.Users.Add(user); db.Units.Add(unit); db.Operations.Add(op); await db.SaveChangesAsync();
        var product = new BaseProduct { FactoryId = factory.Id, Code = "P1", Name = "产品", UnitId = unit.Id, CreatedAt = now };
        db.Products.Add(product); await db.SaveChangesAsync();
        var order = new ProdWorkOrder { FactoryId = factory.Id, OrderNo = "WO", ProductId = product.Id, Qty = 100, Status = 0, CreatedAt = now };
        db.WorkOrders.Add(order);
        db.WechatAlertSettings.Add(new() { FactoryId = factory.Id, Enabled = true, WebhookKey = "fixture-key-" + factory.Id, CorpId = "corp", Secret = "secret", AgentId = "1", ToUser = users, DailyLimit = limit, UpdatedAt = now });
        await db.SaveChangesAsync();
        db.WorkOrderOperations.Add(new() { WorkOrderId = order.Id, OperationId = op.Id, Seq = 1, PlanQty = 100 }); await db.SaveChangesAsync();
        foreach (var type in new[] { "report_created", "order_started", "order_completed" })
            await svc.SaveRuleAsync(null, new() { Name = type, EventType = type, Enabled = true, ToUser = users, Template = "{工单号} {产品编号} {产品名称}" + (type == "report_created" ? " {工序} {良品数} {不良数} {不良率}" : "") }, factory.Id);
        return (order, user, op);
    }

    [Fact]
    public async Task SubmitReplayBatchAndFinalStatus()
    {
        await using var db = fixture.Open(); var (events, _) = Services(db);
        var (order, user, op) = await SeedAsync(db, events);
        var report = new ReportService(db, events);
        var dto = new ReportDto { OrderId = order.Id, OperationId = op.Id, GoodQty = 10, ClientRequestId = "req-1" };
        await report.SubmitAsync(dto, user.Id); await report.SubmitAsync(dto, user.Id);
        var rows = await db.WechatAlertDeliveries.Where(x => x.FactoryId == order.FactoryId).ToListAsync();
        Assert.Equal(2, rows.Count); Assert.Single(rows, x => x.EventType == "report_created"); Assert.Single(rows, x => x.EventType == "order_started");
        await report.BatchReportAsync(new() { OrderId = order.Id, GoodQty = 90, BatchNo = "batch-1" }, user.Id);
        await report.BatchReportAsync(new() { OrderId = order.Id, GoodQty = 90, BatchNo = "batch-1" }, user.Id);
        Assert.Equal(4, await db.WechatAlertDeliveries.CountAsync(x => x.FactoryId == order.FactoryId));
    }

    [Fact]
    public async Task RollbackNoDeliveryAndUnauthorizedNoEnqueue()
    {
        await using var db = fixture.Open(); var license = new FakeLicense(); var (events, _) = Services(db, license);
        var (order, _, _) = await SeedAsync(db, events);
        await using (var tx = await db.Database.BeginTransactionAsync()) { order.Status = 1; await db.SaveChangesAsync(); await events.EnqueueAsync(order, 0); await tx.RollbackAsync(); }
        db.ChangeTracker.Clear(); Assert.Empty(await db.WechatAlertDeliveries.Where(x => x.FactoryId == order.FactoryId).ToListAsync());
        license.Allowed = false;
        var wo = new WorkOrderService(db, events); await wo.TransitionAsync(order.Id, "start", order.FactoryId);
        Assert.Empty(await db.WechatAlertDeliveries.Where(x => x.FactoryId == order.FactoryId).ToListAsync());
    }

    [Fact]
    public async Task CompleteDirectlyDoesNotSendStartAndWithdrawDoesNotSendStart()
    {
        await using var db = fixture.Open(); var (events, _) = Services(db); var (order, user, op) = await SeedAsync(db, events);
        await new ReportService(db, events).SubmitAsync(new() { OrderId = order.Id, OperationId = op.Id, GoodQty = 100 }, user.Id);
        Assert.False(await db.WechatAlertDeliveries.AnyAsync(x => x.FactoryId == order.FactoryId && x.EventType == "order_started"));
        var wo = new WorkOrderService(db, events); await wo.TransitionAsync(order.Id, "withdraw", order.FactoryId); await wo.TransitionAsync(order.Id, "finish", order.FactoryId);
        Assert.Equal(2, await db.WechatAlertDeliveries.CountAsync(x => x.FactoryId == order.FactoryId && x.EventType == "order_completed"));
    }

    [Fact]
    public async Task TimeoutIsUnknownNoAutomaticResendAndVerificationAudited()
    {
        await using var db = fixture.Open(); var http = new FakeHttp { FailMessage = true }; var (events, _) = Services(db, http: http);
        var (order, user, _) = await SeedAsync(db, events); await new WorkOrderService(db, events).TransitionAsync(order.Id, "start", order.FactoryId);
        var first = await events.DispatchAsync(); Assert.NotEqual(0, first.Code); await events.DispatchAsync(); Assert.Single(http.Recipients);
        var row = await db.WechatAlertDeliveries.AsNoTracking().SingleAsync(x => x.FactoryId == order.FactoryId); Assert.Equal("unknown", row.Status);
        var attempt = await db.WechatSendAttempts.SingleAsync(x => x.DeliveryId == row.Id); Assert.DoesNotContain("test-secret-token", attempt.Error ?? ""); Assert.DoesNotContain("access_token=test", attempt.Error ?? ""); Assert.True(attempt.Charged);
        await Assert.ThrowsAsync<BusinessException>(() => events.HandleAsync(order.FactoryId, row.Id, user.Id, new(), false));
        await events.HandleAsync(order.FactoryId, row.Id, user.Id, new() { Sent = true, Note = "接收人已确认" }, true);
        db.ChangeTracker.Clear(); row = await db.WechatAlertDeliveries.SingleAsync(x => x.Id == row.Id); Assert.Equal("confirmed", row.Status); Assert.Equal(user.Id, row.HandledBy);
        Assert.False(await db.WechatAlertLogs.AnyAsync(x => x.FactoryId == order.FactoryId));
    }

    [Fact]
    public async Task GroupFailureRetryKeepsReceiverSnapshot()
    {
        await using var db = fixture.Open(); var http = new FakeHttp { MessageResponse = "{\"errcode\":93000}" }; var (events, _) = Services(db, http: http);
        var (order, user, _) = await SeedAsync(db, events); await new WorkOrderService(db, events).TransitionAsync(order.Id, "start", order.FactoryId);
        await events.DispatchAsync(); var row = await db.WechatAlertDeliveries.AsNoTracking().SingleAsync(x => x.FactoryId == order.FactoryId); Assert.Equal("failed", row.Status);
        await events.HandleAsync(order.FactoryId, row.Id, user.Id, new(), false);
        await Assert.ThrowsAsync<BusinessException>(() => events.HandleAsync(order.FactoryId, row.Id, user.Id, new(), false));
        http.MessageResponse = "{\"errcode\":0}"; await events.DispatchAsync();
        Assert.Equal(new[] { "Alice|Bob", "Alice|Bob" }, http.Recipients);
        db.ChangeTracker.Clear(); row = await db.WechatAlertDeliveries.SingleAsync(x => x.Id == row.Id); Assert.Equal("success", row.Status); Assert.Equal(2, row.Attempts);
        Assert.Single(await db.WechatAlertLogs.Where(x => x.FactoryId == order.FactoryId).ToListAsync());
    }

    [Fact]
    public async Task LegacyAndEventShareLimitAndTestDoesNotCharge()
    {
        await using var db = fixture.Open(); var http = new FakeHttp(); var (events, sender) = Services(db, http: http);
        var (order, _, _) = await SeedAsync(db, events, limit: 1); await new WorkOrderService(db, events).TransitionAsync(order.Id, "start", order.FactoryId);
        Assert.Equal("success", (await sender.SendAsync(order.FactoryId, "due-alert:test", "交期")).Outcome);
        Assert.Equal("dedup", (await sender.SendAsync(order.FactoryId, "due-alert:test", "交期")).Outcome);
        await events.DispatchAsync(); Assert.Single(http.Recipients);
        Assert.Equal("success", (await sender.SendAsync(order.FactoryId, "test", "测试", test: true)).Outcome); Assert.Equal(2, http.Recipients.Count);
        Assert.Equal(1, await db.WechatSendAttempts.CountAsync(x => x.FactoryId == order.FactoryId && x.Charged));
        var row = await db.WechatAlertDeliveries.AsNoTracking().SingleAsync(x => x.FactoryId == order.FactoryId); Assert.Equal("pending", row.Status); Assert.Equal(DateTime.Today.AddDays(1), row.NextAttemptAt);
    }

    [Fact]
    public async Task ExpiredSendingLeaseUnknownAndRuleStopCancelsPending()
    {
        await using var db = fixture.Open(); var http = new FakeHttp(); var (events, _) = Services(db, http: http); var (order, _, _) = await SeedAsync(db, events);
        await new WorkOrderService(db, events).TransitionAsync(order.Id, "start", order.FactoryId);
        await db.WechatAlertDeliveries.Where(x => x.FactoryId == order.FactoryId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "processing").SetProperty(x => x.SendingStarted, true).SetProperty(x => x.LeaseUntil, DateTime.Now.AddMinutes(-1)));
        var recovered = await events.DispatchAsync(); Assert.NotEqual(0, recovered.Code); Assert.Empty(http.Recipients);
        Assert.Equal("unknown", (await db.WechatAlertDeliveries.AsNoTracking().SingleAsync(x => x.FactoryId == order.FactoryId)).Status);
        await new WorkOrderService(db, events).TransitionAsync(order.Id, "finish", order.FactoryId);
        var rule = await db.WechatAlertRules.SingleAsync(x => x.FactoryId == order.FactoryId && x.EventType == "order_completed");
        await events.SaveRuleAsync(rule.Id, new() { Name = rule.Name, EventType = rule.EventType, Template = rule.Template, Enabled = false }, order.FactoryId);
        Assert.Equal("cancelled", (await db.WechatAlertDeliveries.AsNoTracking().SingleAsync(x => x.FactoryId == order.FactoryId && x.RuleId == rule.Id)).Status);
    }

    [Fact]
    public async Task ExplicitSessionNeverCallsTokenOrMessage()
    {
        await using var db = fixture.Open(); var http = new FakeHttp(); var (events, sender) = Services(db, http: http);
        var (order, _, _) = await SeedAsync(db, events);
        Assert.Equal("cancelled", (await sender.SendAsync(order.FactoryId, "old-session", "消息", channel: "session")).Outcome);
        Assert.Empty(http.Paths);
        Assert.Empty(await db.WechatSendAttempts.Where(x => x.FactoryId == order.FactoryId).ToListAsync());
    }

    [Fact]
    public async Task OverlappingDispatchClaimsOnlyOnceAndSharesQuotaWithLegacy()
    {
        await using var db = fixture.Open(); var http = new FakeHttp { DelayMessages = true }; var (events, _) = Services(db, http: http);
        var (order, _, _) = await SeedAsync(db, events, limit: 1); await new WorkOrderService(db, events).TransitionAsync(order.Id, "start", order.FactoryId);
        await using var db2 = fixture.Open(); var (events2, sender2) = Services(db2, http: http);
        await Task.WhenAll(events.DispatchAsync(), events2.DispatchAsync());
        Assert.Single(http.Recipients); Assert.Equal("success", (await db.WechatAlertDeliveries.AsNoTracking().SingleAsync(x => x.FactoryId == order.FactoryId)).Status);
        Assert.Equal("limited", (await sender2.SendAsync(order.FactoryId, "due-other", "交期")).Outcome);
    }

    [Fact]
    public async Task PendingInsertFailureRollsBackReportAndStatus()
    {
        await using var db = fixture.Open(); var (events, _) = Services(db); var (order, user, op) = await SeedAsync(db, events);
        const string trigger = "CREATE TRIGGER test129_reject_delivery ON sys_wechat_alert_delivery AFTER INSERT AS THROW 51000, 'test delivery insert failure', 1;";
        await db.Database.ExecuteSqlRawAsync(trigger);
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => new ReportService(db, events).SubmitAsync(new() { OrderId = order.Id, OperationId = op.Id, GoodQty = 10 }, user.Id));
            db.ChangeTracker.Clear();
            Assert.Equal(0, await db.Reports.CountAsync(x => x.OrderId == order.Id));
            Assert.Equal(0, await db.WechatAlertDeliveries.CountAsync(x => x.FactoryId == order.FactoryId));
            Assert.Equal((byte)0, (await db.WorkOrders.SingleAsync(x => x.Id == order.Id)).Status);
        }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER test129_reject_delivery"); }
    }

    [Fact]
    public async Task MultiPersonThresholdUsesEachRecordAndPreservesReceiverSnapshot()
    {
        await using var db = fixture.Open(); var http = new FakeHttp(); var (events, _) = Services(db, http: http);
        var (order, admin, op) = await SeedAsync(db, events);
        var other = new SysUser { FactoryId = order.FactoryId, Account = "other", Name = "另一管理员", Password = "x", Role = 1, Status = 1, CreatedAt = DateTime.Now };
        var defect = new BaseDefectItem { FactoryId = order.FactoryId, Name = "不良", CreatedAt = DateTime.Now };
        db.Users.Add(other); db.DefectItems.Add(defect); await db.SaveChangesAsync();
        await events.SaveRuleAsync(null, new() { Name = "高不良率", EventType = "report_created", ConditionType = "defect_rate", Threshold = 10, Enabled = true,
            ToUser = "Quality", Template = "{工序} {良品数} {不良数} {不良率}" }, order.FactoryId);
        var rule = await db.WechatAlertRules.SingleAsync(x => x.FactoryId == order.FactoryId && x.Name == "高不良率");
        await new ReportService(db, events).SubmitAsync(new() { OrderId = order.Id, OperationId = op.Id,
            Assignees = [new() { UserId = admin.Id, GoodQty = 4, DefectQty = 1, DefectId = defect.Id }, new() { UserId = other.Id, GoodQty = 9, DefectQty = 1, DefectId = defect.Id }] }, admin.Id);
        var matching = await db.WechatAlertDeliveries.AsNoTracking().SingleAsync(x => x.RuleId == rule.Id);
        Assert.Contains("4 1 20.00%", matching.Content);
        Assert.Equal(2, await db.WechatAlertDeliveries.CountAsync(x => x.FactoryId == order.FactoryId && x.EventType == "report_created" && x.RuleId != rule.Id));
        await events.SaveRuleAsync(rule.Id, new() { Name = rule.Name, EventType = rule.EventType, ConditionType = rule.ConditionType, Threshold = rule.Threshold,
            Enabled = true, ToUser = "Changed", Template = "新模板" }, order.FactoryId);
        await events.DispatchAsync(); Assert.Contains("Quality", http.Recipients); Assert.DoesNotContain("Changed", http.Recipients);
        Assert.Equal("Alice|Bob", (await db.WechatAlertSettings.SingleAsync(x => x.FactoryId == order.FactoryId)).ToUser);
    }

    [Fact]
    public async Task UpgradeFromOriginalSchemaKeepsAndCountsLegacyLog()
    {
        await using var db = fixture.Open();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE sys_wechat_send_attempt; DROP TABLE sys_wechat_alert_delivery; DROP TABLE sys_wechat_alert_rule;");
        db.WechatAlertLogs.Add(new() { FactoryId = 99999, AlertKey = "legacy", SendDate = DateTime.Today, Content = "旧消息", CreatedAt = DateTime.Now });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(WechatEventSchema.UpgradeSql); await db.Database.ExecuteSqlRawAsync(WechatEventSchema.UpgradeSql);
        await db.Database.ExecuteSqlRawAsync(WechatEventSchema.GroupUpgradeSql);
        await db.Database.ExecuteSqlRawAsync(WechatEventSchema.WebhookOnlyUpgradeSql);
        Assert.Equal(1, await db.WechatSendAttempts.CountAsync(x => x.FactoryId == 99999 && x.Charged));
        Assert.Equal(1, await db.WechatAlertLogs.CountAsync(x => x.FactoryId == 99999));
    }

    [Fact]
    public async Task LegacyFailureThenUnknownMustBlockFurtherResend()
    {
        await using var db = fixture.Open(); var http = new FakeHttp { MessageResponse = "{\"errcode\":81013}" }; var (events, sender) = Services(db, http: http);
        var (order, _, _) = await SeedAsync(db, events);
        Assert.Equal("failed", (await sender.SendAsync(order.FactoryId, "legacy-unknown", "消息")).Outcome);
        http.FailMessage = true;
        Assert.Equal("unknown", (await sender.SendAsync(order.FactoryId, "legacy-unknown", "消息")).Outcome);
        Assert.Equal("unknown", (await sender.SendAsync(order.FactoryId, "legacy-unknown", "消息")).Outcome);
        Assert.Equal(2, http.Recipients.Count);
    }

    [Fact]
    public async Task CrossFactoryCannotEditRetryAndEmptyCronRejected()
    {
        await using var db = fixture.Open(); var license = new FakeLicense(); var http = new FakeHttp(); var (events, sender) = Services(db, license, http); var (order, _, _) = await SeedAsync(db, events);
        var rule = await db.WechatAlertRules.FirstAsync(x => x.FactoryId == order.FactoryId);
        await Assert.ThrowsAsync<BusinessException>(() => events.SaveRuleAsync(rule.Id, new() { Name = "x", Template = "{工单号}" }, order.FactoryId + 10000));
        var legacy = new UnusedAlert();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["WechatAlert:CronToken"] = "correct" }).Build();
        var controller = new WechatAlertController(legacy, config, events) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        await Assert.ThrowsAsync<BusinessException>(() => controller.DispatchEvents());
        controller.Request.Headers["X-Cron-Token"] = "wrong"; await Assert.ThrowsAsync<BusinessException>(() => controller.DispatchEvents());
    }

    [Fact]
    public async Task GroupWebhookSkipsTokenAndAbnormalTypeFilters()
    {
        await using var db = fixture.Open(); var http = new FakeHttp(); var (events, _) = Services(db, http: http);
        var (order, user, _) = await SeedAsync(db, events);
        var setting = await db.WechatAlertSettings.SingleAsync(x => x.FactoryId == order.FactoryId);
        setting.WebhookKey = "group-key-uuid";
        await db.SaveChangesAsync();
        await events.SaveRuleAsync(null, new()
        {
            Name = "物料群", EventType = "abnormal_reported", ConditionType = "abnormal_material",
            TargetChannel = "group", ToUser = "Warehouse", Enabled = true,
            Template = "{异常类型} {问题描述} {上报人} {工单号}"
        }, order.FactoryId);
        await events.SaveRuleAsync(null, new()
        {
            Name = "设备群", EventType = "abnormal_reported", ConditionType = "abnormal_device",
            TargetChannel = "group", Enabled = true,
            Template = "{异常类型} {问题描述}"
        }, order.FactoryId);
        var reporter = await db.Users.AsNoTracking().SingleAsync(x => x.Id == user.Id);
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var material = new ProdAbnormal { FactoryId = order.FactoryId, AbnormalType = 2, Description = "缺料", ReportedBy = user.Id, ReportedAt = DateTime.Now, WorkOrderId = order.Id };
            db.Abnormals.Add(material); await db.SaveChangesAsync();
            await events.EnqueueAbnormalAsync(material, reporter);
            await tx.CommitAsync();
        }
        Assert.Single(await db.WechatAlertDeliveries.Where(x => x.FactoryId == order.FactoryId && x.EventType == "abnormal_reported").ToListAsync());
        await events.DispatchAsync();
        Assert.Contains(http.Paths, p => p.Contains("webhook/send"));
        Assert.DoesNotContain(http.Paths, p => p.EndsWith("gettoken"));
        Assert.Equal("Warehouse", Assert.Single(http.Recipients));
        Assert.All(http.ClientNames, name => Assert.Equal(WechatMessageSender.GroupHttpClientName, name));
        var attempt = await db.WechatSendAttempts.SingleAsync(x => x.FactoryId == order.FactoryId && x.DeliveryId != null);
        Assert.DoesNotContain("group-key", attempt.Error ?? "");
        db.ChangeTracker.Clear();
        Assert.Equal("success", (await db.WechatAlertDeliveries.AsNoTracking().SingleAsync(x => x.FactoryId == order.FactoryId && x.EventType == "abnormal_reported")).Status);
    }

    [Fact]
    public async Task GroupWithoutWebhookDoesNotEnqueue()
    {
        await using var db = fixture.Open(); var (events, _) = Services(db);
        var (order, _, _) = await SeedAsync(db, events);
        await db.WechatAlertSettings.Where(x => x.FactoryId == order.FactoryId).ExecuteUpdateAsync(x => x.SetProperty(s => s.WebhookKey, (string?)null));
        await events.SaveRuleAsync(null, new()
        {
            Name = "无key", EventType = "order_started", TargetChannel = "group", Enabled = false,
            Template = "{工单号} {产品编号} {产品名称}"
        }, order.FactoryId);
        var rule = await db.WechatAlertRules.SingleAsync(x => x.FactoryId == order.FactoryId && x.Name == "无key");
        rule.Enabled = true; rule.TargetChannel = "group"; await db.SaveChangesAsync();
        await new WorkOrderService(db, events).TransitionAsync(order.Id, "start", order.FactoryId);
        Assert.False(await db.WechatAlertDeliveries.AnyAsync(x => x.FactoryId == order.FactoryId && x.RuleId == rule.Id));
    }

    private sealed class FixedOptions<T>(T value) : Microsoft.Extensions.Options.IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static WechatAlertService AlertService(AppDbContext db, FakeLicense license, FakeHttp http, WechatMessageSender sender) =>
        new(db, http, new OutboundHttpErrorLogger(NullLogger<OutboundHttpErrorLogger>.Instance,
            new FixedOptions<ErrorLoggingOptions>(new())), license, sender);

    [Fact]
    public async Task SettingPreservesLegacyAndMentionsWhileMaskAndClearWork()
    {
        await using var db = fixture.Open(); var http = new FakeHttp(); var license = new FakeLicense();
        var (events, sender) = Services(db, license, http); var (order, _, _) = await SeedAsync(db, events);
        var alert = AlertService(db, license, http, sender);
        await alert.SaveSettingAsync(new() { Enabled = true, WebhookKey = "********", ToUser = "Alice,@all", DailyLimit = 30 }, order.FactoryId);
        db.ChangeTracker.Clear(); var setting = await db.WechatAlertSettings.SingleAsync(x => x.FactoryId == order.FactoryId);
        Assert.Equal("corp", setting.CorpId); Assert.Equal("secret", setting.Secret); Assert.Equal("1", setting.AgentId);
        Assert.Equal("@all", setting.ToUser); Assert.Equal("fixture-key-" + order.FactoryId, setting.WebhookKey);
        Assert.Equal("********", (await alert.GetSettingAsync(order.FactoryId)).Data!.WebhookKey);
        await alert.TestSendAsync(order.FactoryId); Assert.Equal("@all", Assert.Single(http.Recipients));
        await Assert.ThrowsAsync<BusinessException>(() => alert.SaveSettingAsync(new() { Enabled = true, WebhookKey = "" }, order.FactoryId));
        db.ChangeTracker.Clear();
        await alert.SaveSettingAsync(new() { Enabled = false, WebhookKey = "", ToUser = "" }, order.FactoryId);
        db.ChangeTracker.Clear(); setting = await db.WechatAlertSettings.SingleAsync(x => x.FactoryId == order.FactoryId);
        Assert.Null(setting.WebhookKey); Assert.Equal("secret", setting.Secret);
        await Assert.ThrowsAsync<BusinessException>(() => alert.TestSendAsync(order.FactoryId)); Assert.Single(http.Recipients);
    }

    [Fact]
    public async Task IndependentRuleCanEnableBeforeMainSwitchAndClearingRestoresDefault()
    {
        await using var db = fixture.Open(); var http = new FakeHttp(); var license = new FakeLicense(); var (events, sender) = Services(db, license, http);
        var (order, _, _) = await SeedAsync(db, events); var alert = AlertService(db, license, http, sender);
        await alert.SaveSettingAsync(new() { Enabled = false, WebhookKey = "" }, order.FactoryId);
        await events.SaveRuleAsync(null, new() { Name = "独立群", Enabled = true, WebhookKey = "independent-key", ToUser = "@all", Template = "{工单号}" }, order.FactoryId);
        await alert.SaveSettingAsync(new() { Enabled = true, WebhookKey = "" }, order.FactoryId);
        await Assert.ThrowsAsync<BusinessException>(() => alert.TestSendAsync(order.FactoryId)); Assert.Empty(http.Paths);
        var rule = await db.WechatAlertRules.AsNoTracking().SingleAsync(x => x.FactoryId == order.FactoryId && x.Name == "独立群");
        await events.SaveRuleAsync(rule.Id, new() { Name = rule.Name, Template = rule.Template, WebhookKey = "********", Enabled = true }, order.FactoryId);
        db.ChangeTracker.Clear(); Assert.Equal("independent-key", (await db.WechatAlertRules.SingleAsync(x => x.Id == rule.Id)).WebhookKey);
        await Assert.ThrowsAsync<BusinessException>(() => events.SaveRuleAsync(rule.Id,
            new() { Name = rule.Name, Template = rule.Template, WebhookKey = "", Enabled = true }, order.FactoryId));
        await alert.SaveSettingAsync(new() { Enabled = true, WebhookKey = "global-key" }, order.FactoryId);
        await events.SaveRuleAsync(rule.Id, new() { Name = rule.Name, Template = rule.Template, WebhookKey = "", Enabled = true }, order.FactoryId);
        db.ChangeTracker.Clear(); Assert.Null((await db.WechatAlertRules.SingleAsync(x => x.Id == rule.Id)).WebhookKey);
    }

    [Fact]
    public async Task Existing129TableWithoutGroupColumnsUpgradesInOneStartup()
    {
        await using var db = fixture.Open(); var (events, _) = Services(db); var (order, _, _) = await SeedAsync(db, events);
        var ruleId = (await db.WechatAlertRules.FirstAsync(x => x.FactoryId == order.FactoryId)).Id;
        await db.Database.ExecuteSqlRawAsync("""
ALTER TABLE sys_wechat_alert_rule DROP CONSTRAINT ck_wechat_rule_channel;
DECLARE @drop nvarchar(max)=N'';
SELECT @drop=@drop+N'ALTER TABLE sys_wechat_alert_rule DROP CONSTRAINT '+QUOTENAME(d.name)+N';'
FROM sys.default_constraints d JOIN sys.columns c ON c.object_id=d.parent_object_id AND c.column_id=d.parent_column_id
WHERE d.parent_object_id=OBJECT_ID(N'sys_wechat_alert_rule') AND c.name=N'target_channel';
EXEC sys.sp_executesql @drop;
ALTER TABLE sys_wechat_alert_rule DROP COLUMN target_channel,webhook_key;
ALTER TABLE sys_wechat_alert_setting DROP COLUMN webhook_key;
DROP INDEX ix_wechat_attempt_robot_rate ON sys_wechat_send_attempt;
ALTER TABLE sys_wechat_send_attempt DROP COLUMN robot_key_hash;
""");
        db.ChangeTracker.Clear();
        await db.Database.ExecuteSqlRawAsync(WechatEventSchema.GroupUpgradeSql);
        await db.Database.ExecuteSqlRawAsync(WechatEventSchema.WebhookOnlyUpgradeSql);
        await db.Database.ExecuteSqlRawAsync(WechatEventSchema.GroupUpgradeSql);
        await db.Database.ExecuteSqlRawAsync(WechatEventSchema.WebhookOnlyUpgradeSql);
        var rule = await db.WechatAlertRules.SingleAsync(x => x.Id == ruleId);
        Assert.Equal("session", rule.TargetChannel); Assert.False(rule.Enabled); Assert.Equal("Alice|Bob", rule.ToUser);
        Assert.Equal("secret", (await db.WechatAlertSettings.SingleAsync(x => x.FactoryId == order.FactoryId)).Secret);
    }

    [Fact]
    public async Task OldRulesAndUnfinishedDeliveriesStopWithoutDestroyingHistory()
    {
        await using var db = fixture.Open(); var http = new FakeHttp(); var (events, sender) = Services(db, http: http);
        var (order, user, _) = await SeedAsync(db, events);
        await Assert.ThrowsAsync<BusinessException>(() => events.SaveRuleAsync(null,
            new() { Name = "旧通道", TargetChannel = "session", Template = "{工单号}" }, order.FactoryId));
        var rule = await db.WechatAlertRules.FirstAsync(x => x.FactoryId == order.FactoryId);
        rule.TargetChannel = "session"; await db.SaveChangesAsync();
        var states = new[] { "pending", "processing", "success" };
        foreach (var state in states) db.WechatAlertDeliveries.Add(new() { FactoryId = order.FactoryId, RuleId = rule.Id,
            RuleName = rule.Name, EventType = rule.EventType, EventId = Guid.NewGuid().ToString("N"), ToUser = "Alice", Content = "旧内容",
            OccurredAt = DateTime.Now, Status = state, SendingStarted = state == "processing", NextAttemptAt = DateTime.Now,
            LeaseId = Guid.NewGuid(), LeaseUntil = DateTime.Now.AddMinutes(2) });
        await db.SaveChangesAsync();
        var inflight = await db.WechatAlertDeliveries.AsNoTracking().SingleAsync(x => x.FactoryId == order.FactoryId && x.Status == "processing");
        Assert.Equal("cancelled", (await sender.SendAsync(order.FactoryId, "old-flight", "旧内容", "Alice", inflight.Id, inflight.LeaseId, channel: "group")).Outcome);
        await db.Database.ExecuteSqlRawAsync(WechatEventSchema.WebhookOnlyUpgradeSql);
        await db.Database.ExecuteSqlRawAsync(WechatEventSchema.WebhookOnlyUpgradeSql);
        db.ChangeTracker.Clear(); rule = await db.WechatAlertRules.SingleAsync(x => x.Id == rule.Id); Assert.False(rule.Enabled); Assert.Equal("session", rule.TargetChannel);
        var rows = await db.WechatAlertDeliveries.Where(x => x.FactoryId == order.FactoryId).ToListAsync();
        Assert.Single(rows, x => x.Status == "cancelled" && !string.IsNullOrEmpty(x.Error)); Assert.Single(rows, x => x.Status == "unknown"); Assert.Single(rows, x => x.Status == "success");
        Assert.Equal("secret", (await db.WechatAlertSettings.SingleAsync(x => x.FactoryId == order.FactoryId)).Secret);
        await Assert.ThrowsAsync<BusinessException>(() => events.SaveRuleAsync(rule.Id, new() { Name = rule.Name, TargetChannel = "group", Template = rule.Template }, order.FactoryId));
        await Assert.ThrowsAsync<BusinessException>(() => events.HandleAsync(order.FactoryId, inflight.Id, user.Id, new() { Sent = false, Note = "未收到" }, true));
        db.ChangeTracker.Clear();
        await events.HandleAsync(order.FactoryId, inflight.Id, user.Id, new() { Sent = true, Note = "已收到" }, true);
        await events.DispatchAsync(); Assert.Empty(http.Paths);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisabledOrTrialBlocksTestDueEventAndRetry(bool disabled)
    {
        await using var db = fixture.Open(); var http = new FakeHttp(); var license = new FakeLicense(); var (events, sender) = Services(db, license, http);
        var (order, user, _) = await SeedAsync(db, events); var alert = AlertService(db, license, http, sender);
        await new WorkOrderService(db, events).TransitionAsync(order.Id, "start", order.FactoryId);
        order.DueDate = DateTime.Now.AddDays(-1); await db.SaveChangesAsync();
        var row = await db.WechatAlertDeliveries.AsNoTracking().SingleAsync(x => x.FactoryId == order.FactoryId);
        if (disabled) await db.WechatAlertSettings.Where(x => x.FactoryId == order.FactoryId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Enabled, false));
        else license.Allowed = false;
        await Assert.ThrowsAsync<BusinessException>(() => alert.TestSendAsync(order.FactoryId));
        await alert.PushOverdueAsync(order.FactoryId); await alert.PushDueAlertCronAsync(); await events.DispatchAsync();
        await db.WechatAlertDeliveries.Where(x => x.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "failed"));
        await Assert.ThrowsAsync<BusinessException>(() => events.HandleAsync(order.FactoryId, row.Id, user.Id, new(), false));
        Assert.Empty(http.Paths);
    }

    [Fact]
    public async Task DefaultMentionsUsedForDailyBatchAndDedupAcrossCronAndManual()
    {
        await using var db = fixture.Open(); var http = new FakeHttp(); var license = new FakeLicense(); var (events, sender) = Services(db, license, http);
        var (order, _, _) = await SeedAsync(db, events); var alert = AlertService(db, license, http, sender);
        order.DueDate = DateTime.Now.AddDays(-1); await db.SaveChangesAsync();
        await alert.PushOverdueAsync(order.FactoryId); await alert.PushDueAlertCronAsync(); await alert.PushOverdueAsync(order.FactoryId);
        Assert.Equal("Alice|Bob", Assert.Single(http.Recipients));
        Assert.Single(await db.WechatAlertLogs.Where(x => x.FactoryId == order.FactoryId).ToListAsync());
        Assert.DoesNotContain(http.Paths, x => x.EndsWith("gettoken"));
    }

    [Fact]
    public async Task RobotLimitSharedAcrossFactoriesAndFailuresButNotDailyTestQuota()
    {
        await using var db = fixture.Open(); var http = new FakeHttp { MessageResponse = "{\"errcode\":93000,\"errmsg\":\"echo shared-robot-key\"}" };
        var license = new FakeLicense(); var (events, sender) = Services(db, license, http);
        var (first, _, _) = await SeedAsync(db, events); var (second, _, _) = await SeedAsync(db, events);
        await db.WechatAlertSettings.Where(x => x.FactoryId == first.FactoryId || x.FactoryId == second.FactoryId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.WebhookKey, "shared-robot-key"));
        for (var i = 0; i < 20; i++)
            Assert.Equal("failed", (await sender.SendAsync(i % 2 == 0 ? first.FactoryId : second.FactoryId, "test", "限流测试", test: true)).Outcome);
        Assert.Equal("rate_limited", (await sender.SendAsync(second.FactoryId, "test", "限流测试", test: true)).Outcome);
        await new WorkOrderService(db, events).TransitionAsync(first.Id, "start", first.FactoryId);
        await events.DispatchAsync();
        Assert.Equal(20, http.Recipients.Count);
        var pending = await db.WechatAlertDeliveries.AsNoTracking().SingleAsync(x => x.FactoryId == first.FactoryId);
        Assert.Equal("pending", pending.Status); Assert.Equal(0, pending.Attempts); Assert.InRange(pending.NextAttemptAt, DateTime.Now.AddSeconds(45), DateTime.Now.AddSeconds(65));
        Assert.Equal(0, await db.WechatSendAttempts.CountAsync(x => x.Charged));
        Assert.All(await db.WechatSendAttempts.ToListAsync(), x => { Assert.DoesNotContain("shared-robot-key", x.Error ?? ""); Assert.Contains("追踪号", x.Error); });
        // 重启/新服务实例仍读取持久限流；窗口过后可继续。
        await db.WechatSendAttempts.ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, DateTime.Now.AddMinutes(-2)));
        http.MessageResponse = "{\"errcode\":0}";
        await using var db2 = fixture.Open(); var (_, sender2) = Services(db2, license, http);
        Assert.Equal("success", (await sender2.SendAsync(first.FactoryId, "test", "测试", test: true)).Outcome);
    }

    [Fact]
    public async Task SameRobotConcurrentFactoriesCannotExceedWindow()
    {
        await using var db = fixture.Open(); var http = new FakeHttp { DelayMessages = true }; var (events, sender) = Services(db, http: http);
        var (first, _, _) = await SeedAsync(db, events); var (second, _, _) = await SeedAsync(db, events);
        await db.WechatAlertSettings.Where(x => x.FactoryId == first.FactoryId || x.FactoryId == second.FactoryId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.WebhookKey, "parallel-robot"));
        var hash = WechatMessageSender.RobotKeyHash("parallel-robot");
        for (var i = 0; i < 19; i++) db.WechatSendAttempts.Add(new() { FactoryId = first.FactoryId,
            RequestKey = "seed-rate-" + i, AlertKey = "test", SendDate = DateTime.Today, ToUser = "(group)", Outcome = "failed", RobotKeyHash = hash, CreatedAt = DateTime.Now });
        await db.SaveChangesAsync();
        await using var db2 = fixture.Open(); var (_, sender2) = Services(db2, http: http);
        var results = await Task.WhenAll(sender.SendAsync(first.FactoryId, "test", "测试", test: true), sender2.SendAsync(second.FactoryId, "test", "测试", test: true));
        Assert.Single(http.Recipients); Assert.Single(results, x => x.Outcome == "success");
        Assert.Contains(results, x => x.Outcome is "busy" or "rate_limited");
        Assert.Equal(20, await db.WechatSendAttempts.CountAsync(x => x.RobotKeyHash == hash));
    }

    private sealed class UnusedAlert : IWechatAlertService
    {
        public Task<ApiResult<WechatAlertSettingDto>> GetSettingAsync(long factoryId) => throw new NotSupportedException();
        public Task<ApiResult<object?>> SaveSettingAsync(WechatAlertSettingDto dto, long factoryId) => throw new NotSupportedException();
        public Task<ApiResult<object?>> MarkNoticeSeenAsync(long factoryId) => throw new NotSupportedException();
        public Task<ApiResult<object?>> TestSendAsync(long factoryId) => throw new NotSupportedException();
        public Task<ApiResult<object?>> PushOverdueAsync(long factoryId) => throw new NotSupportedException();
        public Task<ApiResult<object?>> PushDueAlertCronAsync() => throw new NotSupportedException();
        public Task SendAsync(long factoryId, string alertKey, string content, string level = "info") => throw new NotSupportedException();
    }
}
