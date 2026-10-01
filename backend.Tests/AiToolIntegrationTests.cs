using System.Text;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace WorkshopMes.Tests;

/// <summary>
/// S06：客服工具与接口契约集成测试（真实 SQL Server + 可控模型模拟，非真实模型）。
/// 覆盖：可控模型响应走通工具往返；未知工具/SQL/路径/权限参数被拒绝；跨工厂/跨用户会话被拒绝；progress/guide 原模式仍有效。
/// 真实模型联调见 S10（本项为模型模拟验证）。
/// </summary>
[Collection("ai_db")]
public class AiToolIntegrationTests
{
    private readonly AiEvidenceDbFixture _fx;
    public AiToolIntegrationTests(AiEvidenceDbFixture fx) { _fx = fx; }

    // ============ 可控模型（模拟） ============
    private sealed class ScriptedDeepSeekClient : IDeepSeekClient
    {
        private readonly Queue<DeepSeekChatResult> _responses = new();
        public ScriptedDeepSeekClient(params DeepSeekChatResult[] responses)
        {
            foreach (var r in responses) _responses.Enqueue(r);
        }
        public Task<DeepSeekChatResult> ChatAsync(IReadOnlyList<DeepSeekChatMessage> messages, IReadOnlyList<DeepSeekToolDef>? tools, CancellationToken ct)
            => Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : new DeepSeekChatResult { Content = "（无脚本响应）" });
        public Task<DeepSeekChatResult> ConnectionTestAsync(CancellationToken ct)
            => Task.FromResult(new DeepSeekChatResult { Content = "ok" });
    }

    /// <summary>第一次调用返回工具调用，之后抛异常（模拟模型故障）。</summary>
    private sealed class ToolThenFailClient : IDeepSeekClient
    {
        private readonly long _orderId;
        private int _calls;
        public ToolThenFailClient(long orderId) { _orderId = orderId; }
        public Task<DeepSeekChatResult> ChatAsync(IReadOnlyList<DeepSeekChatMessage> messages, IReadOnlyList<DeepSeekToolDef>? tools, CancellationToken ct)
        {
            if (++_calls == 1)
                return Task.FromResult(new DeepSeekChatResult
                {
                    ToolCalls = new List<DeepSeekToolCall>
                    {
                        new() { Id = "c1", Function = new DeepSeekFunctionCall { Name = "get_work_order_report_evidence", Arguments = $"{{\"workOrderId\": {_orderId}}}" } }
                    }
                });
            throw new InvalidOperationException("模拟模型故障");
        }
        public Task<DeepSeekChatResult> ConnectionTestAsync(CancellationToken ct)
            => Task.FromResult(new DeepSeekChatResult { Content = "ok" });
    }

    private static AiOptions BuildOptions(string? supportDocsDir)
    {
        var opt = new AiOptions
        {
            Enabled = true,
            ApiKey = "test-key",
            Model = "test-model",
            BusinessTimeZone = "Asia/Shanghai"
        };
        if (supportDocsDir != null)
            opt.SupportDocs = new SupportDocsOptions { Enabled = true, RootPath = supportDocsDir };
        return opt;
    }

    private static (AppDbContext db, AiToolDispatcher dispatcher) BuildDispatcher(AiEvidenceDbFixture fx, string? supportDocsDir)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(AiEvidenceDbFixture.ConnString).Options;
        var db = new AppDbContext(opts);
        var workOrders = new WorkOrderService(db);
        var aiOptions = Options.Create(BuildOptions(supportDocsDir));
        var parser = new KnowledgeFileParser(new ConfigurationBuilder().Build());
        var supportDocs = new SupportDocumentService(aiOptions, NullLogger<SupportDocumentService>.Instance);
        var reportEvidence = new AiReportEvidenceService(db, workOrders, aiOptions, NullLogger<AiReportEvidenceService>.Instance);
        var dispatcher = new AiToolDispatcher(db, workOrders, parser, supportDocs, reportEvidence, aiOptions, NullLogger<AiToolDispatcher>.Instance);
        return (db, dispatcher);
    }

    private static (AppDbContext db, AiAssistantService svc, AiSessionStore sessions) BuildAssistant(
        AiEvidenceDbFixture fx, IDeepSeekClient client, string? supportDocsDir)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(AiEvidenceDbFixture.ConnString).Options;
        var db = new AppDbContext(opts);
        var workOrders = new WorkOrderService(db);
        var aiOptions = Options.Create(BuildOptions(supportDocsDir));
        var parser = new KnowledgeFileParser(new ConfigurationBuilder().Build());
        var supportDocs = new SupportDocumentService(aiOptions, NullLogger<SupportDocumentService>.Instance);
        var reportEvidence = new AiReportEvidenceService(db, workOrders, aiOptions, NullLogger<AiReportEvidenceService>.Instance);
        var dispatcher = new AiToolDispatcher(db, workOrders, parser, supportDocs, reportEvidence, aiOptions, NullLogger<AiToolDispatcher>.Instance);
        var sessions = new AiSessionStore(aiOptions);
        var svc = new AiAssistantService(aiOptions, client, sessions, dispatcher, reportEvidence, NullLogger<AiAssistantService>.Instance);
        return (db, svc, sessions);
    }

    private static string TempDir()
    {
        var p = Path.Combine(Path.GetTempPath(), "ws_support_s06_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    private static void Write(string dir, string name, string content)
        => File.WriteAllText(Path.Combine(dir, name), content, new UTF8Encoding(false));

    private static AiSessionState SupportSession(AiEvidenceDbFixture fx)
        => new() { Mode = "support", FactoryId = fx.FactoryId, UserId = 100 };

    // ============ 可控模型响应走通工具往返 ============

    [Fact]
    public async Task SupportMode_ReportEvidence_ToolRoundTrip()
    {
        var client = new ScriptedDeepSeekClient(
            new DeepSeekChatResult
            {
                ToolCalls = new List<DeepSeekToolCall>
                {
                    new() { Id = "call_1", Function = new DeepSeekFunctionCall { Name = "get_work_order_report_evidence", Arguments = $"{{\"workOrderId\": {_fx.W1}}}" } }
                }
            },
            new DeepSeekChatResult { Content = $"该工单完成数是 80（两道工序有效良品取最小值），证据见 [evt_{_fx.W1}]。" }
        );
        var (_, svc, _) = BuildAssistant(_fx, client, null);

        var res = await svc.SendMessageAsync(new AiMessageRequest { Mode = "support", Message = "为什么完成数是80？" }, 100, _fx.FactoryId, CancellationToken.None);

        Assert.Equal(0, res.Code);
        Assert.NotNull(res.Data!.ReportEvidence);
        Assert.Equal(80, res.Data.ReportEvidence!.Totals.DoneQty);
        Assert.Contains(res.Data.Sources, s => s.SourceId == $"evt_{_fx.W1}");
        Assert.Contains("80", res.Data.Answer);
    }

    [Fact]
    public async Task SupportMode_SearchSupportDocs_ToolRoundTrip()
    {
        var dir = TempDir();
        try
        {
            Write(dir, "01.md", "---\ntitle: 操作入门\nversion: 1.0\nupdatedAt: 2026-09-20\n---\n# 如何补报\n在报工页点批量补报，输入数量，选择工序后保存。");
            var client = new ScriptedDeepSeekClient(
                new DeepSeekChatResult
                {
                    ToolCalls = new List<DeepSeekToolCall>
                    {
                        new() { Id = "call_1", Function = new DeepSeekFunctionCall { Name = "search_support_docs", Arguments = "{\"query\":\"如何补报\"}" } }
                    }
                },
                new DeepSeekChatResult { Content = "补报步骤见来源卡片。" }
            );
            var (_, svc, _) = BuildAssistant(_fx, client, dir);

            var res = await svc.SendMessageAsync(new AiMessageRequest { Mode = "support", Message = "如何补报" }, 100, _fx.FactoryId, CancellationToken.None);

            Assert.Equal(0, res.Code);
            Assert.NotEmpty(res.Data!.Sources);
            Assert.All(res.Data.Sources, s => Assert.Equal("support_doc", s.Type));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ============ 未知工具 / SQL / 路径 / 权限参数被拒绝 ============

    [Fact]
    public async Task Dispatcher_RejectsForbiddenArgs()
    {
        var (_, d) = BuildDispatcher(_fx, null);
        var s = SupportSession(_fx);
        await Assert.ThrowsAsync<BusinessException>(() => d.DispatchAsync("search_support_docs", "{\"query\":\"x\",\"sql\":\"select 1\"}", s, CancellationToken.None));
        await Assert.ThrowsAsync<BusinessException>(() => d.DispatchAsync("get_work_order_report_evidence", "{\"workOrderId\":1,\"path\":\"c:/x\"}", s, CancellationToken.None));
        await Assert.ThrowsAsync<BusinessException>(() => d.DispatchAsync("get_work_order_report_evidence", "{\"workOrderId\":1,\"factoryId\":2}", s, CancellationToken.None));
        await Assert.ThrowsAsync<BusinessException>(() => d.DispatchAsync("get_work_order_report_evidence", "{\"workOrderId\":1,\"userId\":2}", s, CancellationToken.None));
        await Assert.ThrowsAsync<BusinessException>(() => d.DispatchAsync("get_work_order_report_evidence", "{\"workOrderId\":1,\"role\":1}", s, CancellationToken.None));
    }

    [Fact]
    public async Task Dispatcher_RejectsUnknownAndWrongModeTool()
    {
        var (_, d) = BuildDispatcher(_fx, null);
        var s = SupportSession(_fx);
        // 未知工具
        await Assert.ThrowsAsync<BusinessException>(() => d.DispatchAsync("exec_sql", "{}", s, CancellationToken.None));

        // 模式白名单：guide 模式不能调客服数据工具
        var guide = new AiSessionState { Mode = "guide", FactoryId = _fx.FactoryId, UserId = 100 };
        await Assert.ThrowsAsync<BusinessException>(() => d.DispatchAsync("get_work_order_report_evidence", "{\"workOrderId\":1}", guide, CancellationToken.None));
        // support 模式不能调 guide 的工艺工具
        await Assert.ThrowsAsync<BusinessException>(() => d.DispatchAsync("resolve_product", "{\"productCode\":\"x\"}", s, CancellationToken.None));
    }

    // ============ 跨工厂 / 跨用户 / 非 support 会话被拒绝 ============

    [Fact]
    public async Task SearchReportEvidence_RejectsCrossFactoryCrossUserNonSupport()
    {
        var (_, svc, sessions) = BuildAssistant(_fx, new ScriptedDeepSeekClient(), null);
        var sid = sessions.Create(_fx.FactoryId, 100, "support").Id;

        // 跨工厂
        await Assert.ThrowsAsync<BusinessException>(() => svc.SearchReportEvidenceAsync(
            new AiReportEvidenceSearchRequest { ConversationId = sid, WorkOrderId = _fx.W1 }, 100, _fx.FactoryId + 999, CancellationToken.None));
        // 跨用户
        await Assert.ThrowsAsync<BusinessException>(() => svc.SearchReportEvidenceAsync(
            new AiReportEvidenceSearchRequest { ConversationId = sid, WorkOrderId = _fx.W1 }, 101, _fx.FactoryId, CancellationToken.None));
        // 非 support 会话
        var pid = sessions.Create(_fx.FactoryId, 100, "progress").Id;
        await Assert.ThrowsAsync<BusinessException>(() => svc.SearchReportEvidenceAsync(
            new AiReportEvidenceSearchRequest { ConversationId = pid, WorkOrderId = _fx.W1 }, 100, _fx.FactoryId, CancellationToken.None));
        // 缺 conversationId
        await Assert.ThrowsAsync<BusinessException>(() => svc.SearchReportEvidenceAsync(
            new AiReportEvidenceSearchRequest { WorkOrderId = _fx.W1 }, 100, _fx.FactoryId, CancellationToken.None));
    }

    // ============ 翻页接口走通（不调模型） ============

    [Fact]
    public async Task SearchReportEvidence_Paging_Works()
    {
        var (_, svc, sessions) = BuildAssistant(_fx, new ScriptedDeepSeekClient(), null);
        var sid = sessions.Create(_fx.FactoryId, 100, "support").Id;

        var res = await svc.SearchReportEvidenceAsync(
            new AiReportEvidenceSearchRequest { ConversationId = sid, WorkOrderId = _fx.W5, Page = 2, PageSize = 20 },
            100, _fx.FactoryId, CancellationToken.None);

        Assert.Equal(0, res.Code);
        Assert.Equal(55, res.Data!.Total);
        Assert.Equal(20, res.Data.List.Count);
        Assert.Equal(2, res.Data.Page);
    }

    // ============ progress/guide 原模式仍有效 ============

    [Fact]
    public async Task ProgressGuideModes_StillAccepted()
    {
        var client = new ScriptedDeepSeekClient(new DeepSeekChatResult { Content = "就绪" });
        var (_, svc, _) = BuildAssistant(_fx, client, null);
        var p = await svc.SendMessageAsync(new AiMessageRequest { Mode = "progress", Message = "查工单" }, 100, _fx.FactoryId, CancellationToken.None);
        Assert.Equal(0, p.Code);
        var g = await svc.SendMessageAsync(new AiMessageRequest { Mode = "guide", Message = "查工艺" }, 100, _fx.FactoryId, CancellationToken.None);
        Assert.Equal(0, g.Code);
    }

    // ============ 工具定义：support 模式暴露的四个工具 ============

    [Fact]
    public void SupportMode_ToolsAreWhitelisted()
    {
        var (_, d) = BuildDispatcher(_fx, null);
        var names = d.ToolsForMode("support").Select(t => t.Function.Name).ToHashSet();
        Assert.Equal(4, names.Count);
        Assert.Contains("search_support_docs", names);
        Assert.Contains("search_work_orders", names);
        Assert.Contains("get_work_order_progress", names);
        Assert.Contains("get_work_order_report_evidence", names);
    }

    // ============ S07：模型失败仍保留真实证据 ============

    [Fact]
    public async Task ModelFailure_AfterEvidence_PreservesEvidence()
    {
        var (_, svc, _) = BuildAssistant(_fx, new ToolThenFailClient(_fx.W1), null);
        var res = await svc.SendMessageAsync(new AiMessageRequest { Mode = "support", Message = "完成数是多少" }, 100, _fx.FactoryId, CancellationToken.None);

        Assert.Equal(0, res.Code);
        Assert.NotNull(res.Data!.ReportEvidence);
        Assert.Equal(80, res.Data.ReportEvidence!.Totals.DoneQty);
        Assert.Contains("AI 服务暂时不可用，请稍后重试", res.Data.Warnings);
        Assert.Contains("真实证据", res.Data.Answer);
    }

    // ============ S07：伪造来源引用被拒绝 ============

    [Fact]
    public async Task FabricatedCitation_AddsWarning()
    {
        var client = new ScriptedDeepSeekClient(
            new DeepSeekChatResult
            {
                ToolCalls = new List<DeepSeekToolCall>
                {
                    new() { Id = "c1", Function = new DeepSeekFunctionCall { Name = "get_work_order_report_evidence", Arguments = $"{{\"workOrderId\": {_fx.W1}}}" } }
                }
            },
            new DeepSeekChatResult { Content = "完成数是 80，见 [evt_999999]。" }
        );
        var (_, svc, _) = BuildAssistant(_fx, client, null);
        var res = await svc.SendMessageAsync(new AiMessageRequest { Mode = "support", Message = "完成数" }, 100, _fx.FactoryId, CancellationToken.None);

        Assert.Equal(0, res.Code);
        Assert.Contains(res.Data!.Warnings, w => w.Contains("不存在的来源"));
        // 不产生伪造来源卡：来源仍只含真实证据
        Assert.All(res.Data.Sources, s => Assert.Equal($"evt_{_fx.W1}", s.SourceId));
    }
}
