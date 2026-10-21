using System.Collections.Concurrent;
using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace WorkshopMes.Tests;

public class AiSessionLifecycleTests
{
    private static AiSessionStore Store(int perUser = 3, int total = 100) => new(
        Options.Create(new AiOptions { MaxSessionsPerUser = perUser, MaxSessions = total }));

    [Fact]
    public async Task ThinkingHistory_PreservesToolAndFinalReasoningAcrossTurns()
    {
        var (service, _, client) = Assistant();
        var calls = 0;
        const string reasoning = "  思考\n原文  ";
        client.Reply = _ => Task.FromResult(++calls == 1
            ? new DeepSeekChatResult { ReasoningContent = reasoning, ToolCalls = new() { new()
                { Id = "lookup", Function = new() { Name = "search_support_docs", Arguments = "{\"query\":\"扫码\"}" } } } }
            : new DeepSeekChatResult { Content = "已核对。", ReasoningContent = reasoning + calls });
        var first = await service.SendMessageAsync(Request(), 1, 1, CancellationToken.None);
        Assert.Contains(client.LastMessages, m => m.ToolCalls?.Any(t => t.Id == "lookup") == true
            && m.ReasoningContent == reasoning);
        await service.SendMessageAsync(Request(first.Data!.ConversationId), 1, 1, CancellationToken.None);
        Assert.Contains(client.LastMessages, m => m.Content == "已核对。" && m.ReasoningContent == reasoning + "2");
        Assert.All(client.LastMessages.Where(m => m.ToolCalls?.Any(t => t.Id.StartsWith("support_")) == true),
            m => Assert.Equal("", m.ReasoningContent));
        Assert.DoesNotContain("ReasoningContent", JsonSerializer.Serialize(first));
    }

    [Fact]
    public void UserCapacity_RecyclesLeastRecentlyUsed_NotRecentlyAccessedOrBusy()
    {
        var store = Store();
        var recent = store.Create(1, 1, "support");
        var oldest = store.Create(1, 1, "support");
        var active = store.Create(1, 1, "support", beginGenerate: true);
        recent.LastAccessAt = DateTime.UtcNow.AddMinutes(-10);
        oldest.LastAccessAt = DateTime.UtcNow.AddMinutes(-5);
        active.LastAccessAt = DateTime.UtcNow.AddMinutes(-20);
        store.GetOwned(recent.Id, 1, 1);

        var next = store.Create(1, 1, "support", beginGenerate: true);

        Assert.Contains("新对话", Assert.Throws<BusinessException>(() => store.GetOwned(oldest.Id, 1, 1)).Message);
        Assert.Same(recent, store.GetOwned(recent.Id, 1, 1));
        Assert.Same(active, store.GetOwned(active.Id, 1, 1));
        Assert.True(next.Busy);
    }

    [Fact]
    public void GlobalCapacity_RecyclesIdleAcrossUsers_WithoutSharingContext()
    {
        var store = Store(total: 2);
        var oldest = store.Create(1, 1, "support");
        oldest.Messages.Add(new AiSessionMessage { Role = "user", Content = "private" });
        var active = store.Create(2, 1, "support", beginGenerate: true);
        var next = store.Create(3, 1, "support");
        Assert.Throws<BusinessException>(() => store.GetOwned(oldest.Id, 1, 1));
        Assert.Same(active, store.GetOwned(active.Id, 2, 1));
        Assert.Empty(next.Messages);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    public void Ownership_StillEnforced(long factoryId, long userId)
    {
        var store = Store();
        var session = store.Create(1, 1, "support");
        Assert.Contains("无权", Assert.Throws<BusinessException>(() => store.GetOwned(session.Id, factoryId, userId)).Message);
        Assert.Throws<BusinessException>(() => store.DeleteOwned(session.Id, factoryId, userId));
        Assert.Same(session, store.GetOwned(session.Id, 1, 1));
    }

    [Fact]
    public void BusySessions_AreNotExpired_CompletionRestartsIdleTimer()
    {
        var store = Store();
        var session = store.Create(1, 1, "support", beginGenerate: true);
        session.LastAccessAt = DateTime.UtcNow.AddHours(-2);
        store.Create(2, 2, "support"); // 触发过期清理，不能移除生成中的会话
        store.EndGenerate(session);
        Assert.True(session.LastAccessAt > DateTime.UtcNow.AddSeconds(-5));
        Assert.Same(session, store.GetOwned(session.Id, 1, 1));
        session.LastAccessAt = DateTime.UtcNow.AddHours(-2);
        Assert.Throws<BusinessException>(() => store.GetOwned(session.Id, 1, 1));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CapacityAllBusy_RejectsOnlyUntilWorkCompletes(bool sameUser)
    {
        var store = Store(perUser: 1, total: 1);
        var active = store.Create(1, 1, "support", beginGenerate: true);
        var userId = sameUser ? 1 : 2;
        var error = Assert.Throws<BusinessException>(() => store.Create(1, userId, "support", beginGenerate: true));
        Assert.Contains("稍后重试", error.Message);
        Assert.DoesNotContain("结束旧对话", error.Message);
        store.EndGenerate(active);
        Assert.NotNull(store.Create(1, userId, "support", beginGenerate: true));
    }

    [Fact]
    public void EvictedReference_CannotBeginGeneration()
    {
        var store = Store(perUser: 1);
        var old = store.Create(1, 1, "support");
        store.Create(1, 1, "support");
        Assert.Contains("自动回收", Assert.Throws<BusinessException>(() => store.TryBeginGenerate(old)).Message);
    }

    [Fact]
    public void DeletingBusySession_DoesNotReleaseCapacityEarly()
    {
        var store = Store(perUser: 1);
        var active = store.Create(1, 1, "support", beginGenerate: true);
        store.DeleteOwned(active.Id, 1, 1);
        Assert.Throws<BusinessException>(() => store.Create(1, 1, "support", beginGenerate: true));
        store.EndGenerate(active);
        Assert.Throws<BusinessException>(() => store.GetOwned(active.Id, 1, 1));
        Assert.NotNull(store.Create(1, 1, "support"));
    }

    [Fact]
    public void ParallelCreates_ReserveCapacityAtomically()
    {
        var store = Store();
        var accepted = new ConcurrentBag<AiSessionState>();
        Parallel.For(0, 40, _ =>
        {
            try { accepted.Add(store.Create(1, 1, "support", beginGenerate: true)); }
            catch (BusinessException error) { Assert.Contains("处理中", error.Message); }
        });
        Assert.Equal(3, accepted.Count);
        foreach (var session in accepted)
        {
            Assert.Same(session, store.GetOwned(session.Id, 1, 1));
            Assert.False(store.TryBeginGenerate(session));
            store.EndGenerate(session);
        }
        Assert.NotNull(store.Create(1, 1, "support", beginGenerate: true));
    }

    private sealed class ScriptedClient : IDeepSeekClient
    {
        public IReadOnlyList<DeepSeekChatMessage> LastMessages { get; private set; } = Array.Empty<DeepSeekChatMessage>();
        public Func<CancellationToken, Task<DeepSeekChatResult>> Reply { get; set; } =
            _ => Task.FromResult(new DeepSeekChatResult { Content = "已核对。" });
        public Task<DeepSeekChatResult> ChatAsync(IReadOnlyList<DeepSeekChatMessage> messages,
            IReadOnlyList<DeepSeekToolDef>? tools, CancellationToken ct)
        {
            LastMessages = messages;
            return Reply(ct);
        }
        public Task<DeepSeekChatResult> ConnectionTestAsync(CancellationToken ct) => Reply(ct);
    }

    private static (AiAssistantService service, AiSessionStore sessions, ScriptedClient client) Assistant(string? docs = null)
    {
        var options = Options.Create(new AiOptions
        {
            Enabled = true, ApiKey = "test", Model = "test", MaxSessionsPerUser = 1,
            SupportDocs = new SupportDocsOptions { Enabled = docs != null, RootPath = docs ?? "" }
        });
        var sessions = new AiSessionStore(options);
        var client = new ScriptedClient();
        var support = new SupportDocumentService(options, NullLogger<SupportDocumentService>.Instance);
        var tools = new AiToolDispatcher(null!, null!, null!, support, null!, options, NullLogger<AiToolDispatcher>.Instance);
        return (new AiAssistantService(options, client, sessions, tools, null!,
            NullLogger<AiAssistantService>.Instance), sessions, client);
    }

    private static AiMessageRequest Request(string? id = null) =>
        new() { ConversationId = id, Mode = "support", Message = "怎么扫码报工" };

    [Fact]
    public async Task Support_PrefetchesEvidenceBeforeFirstModelCall_RefreshesEachTurn()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai_prefetch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "20.md");
            File.WriteAllText(path, "# 账上多少钱\n金蝶账目还没接进来，现在看不到。\n");
            var (service, _, client) = Assistant(root);
            var first = await service.SendMessageAsync(new() { Mode = "support", Message = "账上多少钱" }, 1, 1, CancellationToken.None);
            var firstTool = Assert.Single(client.LastMessages, m => m.Role == "tool");
            using var firstPayload = JsonDocument.Parse(firstTool.Content!);
            Assert.Contains(firstPayload.RootElement.GetProperty("sources").EnumerateArray(),
                s => s.GetProperty("Excerpt").GetString()!.Contains("现在看不到"));
            Assert.Contains(client.LastMessages, m => m.Role == "assistant"
                && m.ToolCalls!.Any(c => c.Id == firstTool.ToolCallId));
            Assert.NotEmpty(first.Data!.Sources);

            File.WriteAllText(path, "# 材料涨价按新价算\n金蝶料价留有接口，调用金蝶 MCP 集成。\n");
            var second = await service.SendMessageAsync(new() { Mode = "support", ConversationId = first.Data.ConversationId,
                Message = "材料涨价按新价算" }, 1, 1, CancellationToken.None);
            using var secondPayload = JsonDocument.Parse(client.LastMessages.Last(m => m.Role == "tool").Content!);
            Assert.Contains(secondPayload.RootElement.GetProperty("sources").EnumerateArray(),
                s => s.GetProperty("Excerpt").GetString()!.Contains("金蝶 MCP"));
            Assert.All(second.Data!.Sources, s => Assert.DoesNotContain("现在看不到", s.Excerpt));
            Assert.NotEmpty(second.Data.Sources);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("progress")]
    [InlineData("guide")]
    public async Task OtherModes_DoNotPrefetchSupportDocs(string mode)
    {
        var (service, _, client) = Assistant();
        await service.SendMessageAsync(new() { Mode = mode, Message = "查询工单" }, 1, 1, CancellationToken.None);
        Assert.DoesNotContain(client.LastMessages, m => m.Role == "tool");
    }

    [Fact]
    public async Task Support_DisabledKnowledge_DoesNotInventEvidence()
    {
        var (service, _, client) = Assistant();
        var result = await service.SendMessageAsync(Request(), 1, 1, CancellationToken.None);
        Assert.Empty(result.Data!.Sources);
        Assert.Contains("disabled", Assert.Single(client.LastMessages, m => m.Role == "tool").Content);
    }

    private static int CachedCount(AiSessionStore store) =>
        ((System.Collections.IDictionary)typeof(AiSessionStore)
            .GetField("_sessions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(store)!).Count;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstRequestFailureOrTimeout_DoesNotLeaveSession(bool timeout)
    {
        var (service, sessions, client) = Assistant();
        client.Reply = _ => Task.FromException<DeepSeekChatResult>(timeout
            ? new OperationCanceledException("timeout") : new InvalidOperationException("model failed"));
        for (var i = 0; i < 4; i++)
        {
            var error = await Assert.ThrowsAsync<BusinessException>(() => service.SendMessageAsync(Request(), 1, 1, CancellationToken.None));
            Assert.Contains("AI 服务暂时不可用", error.Message);
            Assert.Equal(0, CachedCount(sessions));
        }
        // 同工厂其他用户的已知会话不受首次失败清理影响。
        var other = sessions.Create(1, 2, "support");
        client.Reply = _ => Task.FromResult(new DeepSeekChatResult { Content = "恢复正常。" });
        var response = await service.SendMessageAsync(Request(), 1, 1, CancellationToken.None);
        Assert.True(sessions.GetOwned(response.Data!.ConversationId, 1, 1).HasAnswered);
        Assert.Same(other, sessions.GetOwned(other.Id, 1, 2));
    }

    [Fact]
    public async Task CancelledFirstRequest_ReleasesBusySlot()
    {
        var (service, sessions, client) = Assistant();
        using var cancel = new CancellationTokenSource();
        client.Reply = _ => { cancel.Cancel(); return Task.FromCanceled<DeepSeekChatResult>(cancel.Token); };
        await Assert.ThrowsAsync<BusinessException>(() => service.SendMessageAsync(Request(), 1, 1, cancel.Token));
        Assert.Equal(0, CachedCount(sessions));
        var next = sessions.Create(1, 1, "support", beginGenerate: true);
        Assert.True(next.Busy);
    }

    [Fact]
    public async Task FailureInExistingConversation_PreservesEarlierAnswer()
    {
        var (service, sessions, client) = Assistant();
        var response = await service.SendMessageAsync(Request(), 1, 1, CancellationToken.None);
        var id = response.Data!.ConversationId;
        client.Reply = _ => Task.FromException<DeepSeekChatResult>(new InvalidOperationException("failed"));
        await Assert.ThrowsAsync<BusinessException>(() => service.SendMessageAsync(Request(id), 1, 1, CancellationToken.None));
        var session = sessions.GetOwned(id, 1, 1);
        Assert.False(session.Busy);
        Assert.Contains(session.Messages, m => m.Role == "assistant" && m.Content == "已核对。");
        client.Reply = _ => Task.FromResult(new DeepSeekChatResult { Content = "继续回答。" });
        Assert.Equal(id, (await service.SendMessageAsync(Request(id), 1, 1, CancellationToken.None)).Data!.ConversationId);
    }

    [Fact]
    public async Task ModelFailureWithRealEvidence_PreservesReturnedConversation()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai_session_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "操作.md"), "# 扫码报工\n打开扫码报工页面扫码。");
            var (service, sessions, client) = Assistant(root);
            var calls = 0;
            client.Reply = _ => ++calls == 1
                ? Task.FromResult(new DeepSeekChatResult { ToolCalls = new() { new()
                {
                    Id = "read_docs", Function = new DeepSeekFunctionCall
                    { Name = "search_support_docs", Arguments = "{\"query\":\"扫码报工\"}" }
                } } })
                : Task.FromException<DeepSeekChatResult>(new InvalidOperationException("failed after evidence"));
            var response = await service.SendMessageAsync(Request(), 1, 1, CancellationToken.None);
            Assert.Contains("真实证据", response.Data!.Answer);
            Assert.NotEmpty(response.Data.Sources);
            Assert.True(sessions.GetOwned(response.Data.ConversationId, 1, 1).HasAnswered);
        }
        finally { Directory.Delete(root, true); }
    }
}
