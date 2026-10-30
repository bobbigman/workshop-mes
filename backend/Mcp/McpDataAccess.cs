using System.Text.Json;
using System.Runtime.CompilerServices;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Mcp;

/// <summary>车间查询按当前绑定账号授权；金蝶授权不参与车间数据扩权。</summary>
public static class McpDataAccess
{
    public static async Task<(SysUser? User, string? Hint)> ResolveAsync(
        IMcpWxBindService bind, IConfiguration config, string? channel, string? session, bool adminOnly = false, string? queryAccessToken = null, [CallerMemberName] string toolName = "")
    {
        SysUser? user;
        try { user = await bind.ResolveQueryUserAsync(queryAccessToken, channel, session); }
        catch { bind.LogQueryAccess(toolName, null, false); throw; }
        var allowed = user != null && user.Status == 1 && user.Role is 1 or 2 or 3 && (!adminOnly || user.Role == 1);
        bind.LogQueryAccess(toolName, user?.Id, allowed);
        if (user == null)
            return (null, JsonSerializer.Serialize(new { code = 1, msg = "请调用start_query_auth完成本人网页验证，再传queryAccessToken查询。" }));
        if (user.Status != 1 || user.Role is not (1 or 2 or 3) || (adminOnly && user.Role != 1))
            return (null, JsonSerializer.Serialize(new { code = 1, msg = $"您（{user.Name}）无权查询此数据" }));
        return (user, null);
    }
}
