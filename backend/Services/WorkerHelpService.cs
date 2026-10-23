using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using Microsoft.EntityFrameworkCore;

namespace ahu.MicrosoftMes.Services;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class WorkerHelpRequest
{
    public string Message { get; set; } = "";
    public string? ConversationId { get; set; }
}

public static class WorkerHelpPolicy
{
    public const string Mode = "worker-help";
    public const string ScopeMessage = "这里仅提供系统操作指导，不能查询实时工单、报工、工资或执行复核等操作。请到有权限的对应页面查看，或联系管理员；也可以问我怎么操作。";

    public static bool IsAllowedEndpoint(string path, string method) =>
        (method == "GET" && path.Equals("/api/worker-help/status", StringComparison.OrdinalIgnoreCase))
        || (method == "POST" && path.Equals("/api/worker-help/messages", StringComparison.OrdinalIgnoreCase))
        || (method == "DELETE" && path.StartsWith("/api/worker-help/conversations/", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParseExact(path["/api/worker-help/conversations/".Length..], "N", out _));

    public static bool IsDataOrActionRequest(string message)
    {
        if (Regex.IsMatch(message, "帮我.*(通过|复核|退回|提交|删除|修改|派工|查|算)|执行.*(报工|复核|删除)|查一下|查下|查出|实时", RegexOptions.IgnoreCase)) return true;
        if (Regex.IsMatch(message, "怎么|如何|哪里|在哪|为什么|怎么办|步骤|说明|不能|找不到|失败|报错|出错|不见|不出来|不对|没显示", RegexOptions.IgnoreCase)) return false;
        return Regex.IsMatch(message, "工资|金额|工单|报工|异常|财务|利润|收入|复核|库存|成本|账上|多少钱", RegexOptions.IgnoreCase);
    }
}

/// <summary>只组合受限帮助能力，不更改管理员AI接口的权限。</summary>
public class WorkerHelpService(AppDbContext db, IAiAssistantService assistant, AiSessionStore sessions)
{
    public async Task EnsureUserAsync(long userId, long factoryId, CancellationToken ct)
    {
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.FactoryId == factoryId
            && u.Status == 1 && (u.Role == 1 || u.Role == 2 || u.Role == 3), ct)
            || !await db.Factories.AsNoTracking().AnyAsync(f => f.Id == factoryId && f.TrialCleanedAtUtc == null, ct))
            throw ThrowHelper.BizUser("账号不可用或不属于当前工厂，请重新登录或联系管理员");
    }

    public async Task<ApiResult<object>> StatusAsync(long userId, long factoryId, CancellationToken ct)
    {
        await EnsureUserAsync(userId, factoryId, ct);
        var status = assistant.GetStatus().Data!;
        return ApiResult<object>.Ok(new { status.Enabled, status.Configured });
    }

    public async Task<ApiResult<AiMessageResultDto>> SendAsync(WorkerHelpRequest request, long userId, long factoryId, CancellationToken ct)
    {
        await EnsureUserAsync(userId, factoryId, ct);
        return await assistant.SendMessageAsync(new AiMessageRequest
        {
            Mode = WorkerHelpPolicy.Mode, Message = request.Message, ConversationId = request.ConversationId
        }, userId, factoryId, ct);
    }

    public async Task<ApiResult<object?>> DeleteAsync(string id, long userId, long factoryId, CancellationToken ct)
    {
        await EnsureUserAsync(userId, factoryId, ct);
        sessions.DeleteOwned(id, factoryId, userId, WorkerHelpPolicy.Mode);
        return ApiResult<object?>.OkMsg();
    }
}
