using System.ComponentModel;
using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;
using ModelContextProtocol.Server;

namespace ahu.MicrosoftMes.Mcp;

[McpServerToolType]
public sealed class QueryIdentityMcpTools(McpQueryIdentityService service, IMcpRequestContext context)
{
    [McpServerTool, Description("发起本厂本人只读查询授权；无可信平台身份时先调用。手机号仅定位，不能单独授权。把返回的authorizationUrl原样交给用户，在网页登录本人并验证密码，再将8位码交给verify_query_identity。不得索要聊天密码。")]
    public Task<string> StartQueryAuth([Description("本厂本人11位手机号")] string phone)
        => Run(() => service.StartAsync(context.RequireFactoryId(nameof(StartQueryAuth)), phone));

    [McpServerTool, Description("用本人网页确认后的8位码兑换30分钟queryAccessToken。lookupToken来自start_query_auth；后续查询传queryAccessToken，不传入sessionId，不展示或朗读长凭证。不能用于报工或SQL维护。")]
    public Task<string> VerifyQueryIdentity(string lookupToken, string verificationCode)
        => Run(() => service.VerifyAsync(context.RequireFactoryId(nameof(VerifyQueryIdentity)), lookupToken, verificationCode));

    private static async Task<string> Run(Func<Task<object>> action)
    {
        try { return JsonSerializer.Serialize(new { code = 0, data = await action() }); }
        catch (BusinessException ex) { return JsonSerializer.Serialize(new { code = ex.Code, msg = ex.Message }); }
    }
}
