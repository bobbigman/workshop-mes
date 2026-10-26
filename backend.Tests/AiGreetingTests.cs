using ahu.MicrosoftMes.Services;
using ahu.MicrosoftMes.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace WorkshopMes.Tests;

public class AiGreetingTests
{
    private sealed class CapturingClient : IDeepSeekClient
    {
        public List<string> Prompts { get; } = new();
        public Task<DeepSeekChatResult> ChatAsync(IReadOnlyList<DeepSeekChatMessage> messages,
            IReadOnlyList<DeepSeekToolDef>? tools, CancellationToken ct)
        {
            Prompts.Add(messages.First(m => m.Role == "system").Content!);
            return Task.FromResult(new DeepSeekChatResult { Content = "查询结果已准备好。" });
        }
        public Task<DeepSeekChatResult> ConnectionTestAsync(CancellationToken ct)
            => Task.FromResult(new DeepSeekChatResult { Content = "ok" });
    }

    [Theory]
    [InlineData("progress")]
    [InlineData("support")]
    [InlineData("guide")]
    public async Task Greeting_OnlyFirstCompletedAnswer_AcrossModes(string mode)
    {
        var options = Options.Create(new AiOptions { Enabled = true, ApiKey = "test", Model = "test" });
        var sessions = new AiSessionStore(options);
        var client = new CapturingClient();
        // 可控模型不调用工具；无需数据库或外部模型服务。
        var tools = new AiToolDispatcher(null!, null!, null!, null!, null!, null!, null!, options,
            NullLogger<AiToolDispatcher>.Instance);
        var service = new AiAssistantService(options, client, sessions, tools, null!,
            NullLogger<AiAssistantService>.Instance);
        var session = sessions.Create(1, 2, mode);
        session.Messages.Add(new AiSessionMessage { Role = "system", Content = "old" });
        // 先前失败的提问/工具轮次不能让首次问候消失。
        session.UserTurnCount = 2;
        session.Messages.Add(new AiSessionMessage
        {
            Role = "assistant", Content = "正在查询",
            ToolCalls = new List<DeepSeekToolCall> { new() { Id = "tool1" } }
        });
        var request = new AiMessageRequest { ConversationId = session.Id, Mode = mode, Message = "查进度" };
        await service.SendMessageAsync(request, 2, 1, CancellationToken.None);
        await service.SendMessageAsync(request, 2, 1, CancellationToken.None);
        Assert.Contains("首次回答", client.Prompts[0]);
        Assert.Contains("师傅您好", client.Prompts[0]);
        Assert.Contains("用户在聊天中明确提供", client.Prompts[0]);
        Assert.Contains("不重复称呼", client.Prompts[1]);
        Assert.DoesNotContain("这是本会话首次回答", client.Prompts[1]);
        Assert.True(session.HasAnswered);
    }
}
