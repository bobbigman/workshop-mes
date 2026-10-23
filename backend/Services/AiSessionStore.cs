using ahu.MicrosoftMes.Common;
using Microsoft.Extensions.Options;

namespace ahu.MicrosoftMes.Services;

public class AiSessionMessage
{
    public string Role { get; set; } = ""; // system | user | assistant | tool
    public string? Content { get; set; }
    public string? ReasoningContent { get; set; }
    public string? ToolCallId { get; set; }
    public string? Name { get; set; }
    public List<DeepSeekToolCall>? ToolCalls { get; set; }
}

public class AiSessionState
{
    public string Id { get; set; } = "";
    public long FactoryId { get; set; }
    public long UserId { get; set; }
    public string Mode { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessAt { get; set; }
    public int UserTurnCount { get; set; }
    public bool HasAnswered { get; set; }
    public bool Busy { get; set; }
    internal bool DeleteWhenIdle { get; set; }
    public List<AiSessionMessage> Messages { get; set; } = new();
    public List<AiCandidateDto> Candidates { get; set; } = new();
    public List<AiSourceDto> Sources { get; set; } = new();
    public AiWorkOrderPageDto? LastWorkOrders { get; set; }
    public AiWorkOrderFiltersDto? LastFilters { get; set; }
    public AiReportEvidenceDto? LastReportEvidence { get; set; }
}

/// <summary>进程内会话缓存。重启或过期后作废；不跨进程共享。</summary>
public class AiSessionStore
{
    private readonly Dictionary<string, AiSessionState> _sessions = new();
    private readonly object _gate = new();
    private readonly AiOptions _opt;

    public AiSessionStore(IOptions<AiOptions> opt) { _opt = opt.Value; }

    private static BusinessException ExpiredSession() =>
        ThrowHelper.BizUser("对话上下文已过期或被自动回收，请点击“新对话”重新开始");

    public AiSessionState Create(long factoryId, long userId, string mode, bool beginGenerate = false)
    {
        lock (_gate)
        {
            PurgeExpired();
            var userSessions = _sessions.Values.Where(s => s.FactoryId == factoryId && s.UserId == userId).ToList();
            while (userSessions.Count >= Math.Max(1, _opt.MaxSessionsPerUser))
            {
                var oldest = OldestIdle(userSessions);
                if (oldest == null)
                    throw ThrowHelper.BizUser("你的提问正在处理中，请稍后重试");
                _sessions.Remove(oldest.Id);
                userSessions.Remove(oldest);
            }
            while (_sessions.Count >= Math.Max(1, _opt.MaxSessions))
            {
                var oldest = OldestIdle(_sessions.Values);
                if (oldest == null)
                    throw ThrowHelper.BizUser("AI 正在处理其他提问，请稍后重试");
                _sessions.Remove(oldest.Id);
            }

            var s = new AiSessionState
            {
                Id = Guid.NewGuid().ToString("N"),
                FactoryId = factoryId,
                UserId = userId,
                Mode = mode,
                CreatedAt = DateTime.UtcNow,
                LastAccessAt = DateTime.UtcNow,
                Busy = beginGenerate
            };
            _sessions[s.Id] = s;
            return s;
        }
    }

    public AiSessionState GetOwned(string id, long factoryId, long userId)
    {
        lock (_gate)
        {
            PurgeExpired();
            if (!_sessions.TryGetValue(id, out var s))
                throw ExpiredSession();
            if (s.FactoryId != factoryId || s.UserId != userId)
                throw ThrowHelper.Biz(nameof(GetOwned), "无权访问该会话");
            s.LastAccessAt = DateTime.UtcNow;
            return s;
        }
    }

    public void DeleteOwned(string id, long factoryId, long userId, string? mode = null)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(id, out var s)) return;
            if (s.FactoryId != factoryId || s.UserId != userId || (mode != null && s.Mode != mode))
                throw ThrowHelper.Biz(nameof(DeleteOwned), "无权清除该会话");
            // 删除不能提前释放正在处理的名额，避免请求未结束就绕过并发保护。
            if (s.Busy) s.DeleteWhenIdle = true;
            else _sessions.Remove(id);
        }
    }

    public bool TryBeginGenerate(AiSessionState s)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(s.Id, out var current) || !ReferenceEquals(current, s))
                throw ExpiredSession();
            if (s.Busy) return false;
            if (_sessions.Values.Count(x => x.FactoryId == s.FactoryId && x.UserId == s.UserId && x.Busy)
                >= Math.Max(1, _opt.MaxSessionsPerUser))
                throw ThrowHelper.BizUser("你的提问正在处理中，请稍后重试");
            s.Busy = true;
            s.LastAccessAt = DateTime.UtcNow;
            return true;
        }
    }

    public void EndGenerate(AiSessionState s, bool discard = false)
    {
        lock (_gate)
        {
            s.Busy = false;
            s.LastAccessAt = DateTime.UtcNow;
            if (discard || s.DeleteWhenIdle) _sessions.Remove(s.Id);
        }
    }

    private static AiSessionState? OldestIdle(IEnumerable<AiSessionState> sessions) =>
        sessions.Where(s => !s.Busy).OrderBy(s => s.LastAccessAt).ThenBy(s => s.CreatedAt).FirstOrDefault();

    // 调用方持有 _gate；过期清理和生成状态变更共用同一把锁。
    private void PurgeExpired()
    {
        var idle = TimeSpan.FromMinutes(Math.Max(1, _opt.SessionIdleMinutes));
        var now = DateTime.UtcNow;
        foreach (var kv in _sessions.ToList())
        {
            if (!kv.Value.Busy && now - kv.Value.LastAccessAt > idle)
                _sessions.Remove(kv.Key);
        }
    }
}
