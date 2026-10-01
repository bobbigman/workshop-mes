using System.Collections.Concurrent;
using ahu.MicrosoftMes.Common;
using Microsoft.Extensions.Options;

namespace ahu.MicrosoftMes.Services;

public class AiSessionMessage
{
    public string Role { get; set; } = ""; // system | user | assistant | tool
    public string? Content { get; set; }
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
    public bool Busy { get; set; }
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
    private readonly ConcurrentDictionary<string, AiSessionState> _sessions = new();
    private readonly object _gate = new();
    private readonly AiOptions _opt;

    public AiSessionStore(IOptions<AiOptions> opt) { _opt = opt.Value; }

    public AiSessionState Create(long factoryId, long userId, string mode)
    {
        PurgeExpired();
        lock (_gate)
        {
            var userKeyCount = _sessions.Values.Count(s => s.FactoryId == factoryId && s.UserId == userId);
            if (userKeyCount >= _opt.MaxSessionsPerUser)
                throw ThrowHelper.Biz(nameof(Create), $"同一用户最多 {_opt.MaxSessionsPerUser} 个会话，请先结束旧对话");
            if (_sessions.Count >= _opt.MaxSessions)
                throw ThrowHelper.Biz(nameof(Create), "会话数已达上限，请稍后再试或结束旧对话");

            var s = new AiSessionState
            {
                Id = Guid.NewGuid().ToString("N"),
                FactoryId = factoryId,
                UserId = userId,
                Mode = mode,
                CreatedAt = DateTime.UtcNow,
                LastAccessAt = DateTime.UtcNow
            };
            _sessions[s.Id] = s;
            return s;
        }
    }

    public AiSessionState GetOwned(string id, long factoryId, long userId)
    {
        PurgeExpired();
        if (!_sessions.TryGetValue(id, out var s))
            throw ThrowHelper.Biz(nameof(GetOwned), "会话不存在或已过期，请新建对话");
        if (s.FactoryId != factoryId || s.UserId != userId)
            throw ThrowHelper.Biz(nameof(GetOwned), "无权访问该会话");
        s.LastAccessAt = DateTime.UtcNow;
        return s;
    }

    public void DeleteOwned(string id, long factoryId, long userId)
    {
        if (!_sessions.TryGetValue(id, out var s))
            return;
        if (s.FactoryId != factoryId || s.UserId != userId)
            throw ThrowHelper.Biz(nameof(DeleteOwned), "无权清除该会话");
        _sessions.TryRemove(id, out _);
    }

    public bool TryBeginGenerate(AiSessionState s)
    {
        lock (s)
        {
            if (s.Busy) return false;
            s.Busy = true;
            return true;
        }
    }

    public void EndGenerate(AiSessionState s)
    {
        lock (s) { s.Busy = false; }
    }

    private void PurgeExpired()
    {
        var idle = TimeSpan.FromMinutes(Math.Max(1, _opt.SessionIdleMinutes));
        var now = DateTime.UtcNow;
        foreach (var kv in _sessions)
        {
            if (now - kv.Value.LastAccessAt > idle)
                _sessions.TryRemove(kv.Key, out _);
        }
    }
}
