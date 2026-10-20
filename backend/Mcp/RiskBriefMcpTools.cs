using System.ComponentModel;
using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;
using ModelContextProtocol.Server;

namespace ahu.MicrosoftMes.Mcp;

[McpServerToolType]
public class RiskBriefMcpTools(IRiskBriefService service, IMcpRequestContext context,
    IMcpWxBindService bind, IConfiguration config)
{
    [McpServerTool, Description("query_risk_brief：查询今日或本周综合风险简报（哪些单风险最高/危险的单/哪些要催）。正式模式仅本厂已绑定管理员；演示免绑定但必须定厂。返回服务端计算的Top-N、原因、建议、重点产品标记及备用纯文本，模型只组织120字以内文字，不自行算数。单张做到哪了仍调query_work_order_progress，查列表调query_work_order。")]
    public async Task<string> QueryRiskBrief(
        [Description("today/今日/今天 或 week/本周；默认today")] string? period = null,
        [Description("展示条数1～10，默认3")] int topN = 3,
        [Description("兼容参数，不使用金蝶授权码，不参与扩权")] string? authToken = null,
        [Description("渠道，可空")] string? chanType = null,
        [Description("会话身份，可空")] string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var access = await McpDataAccess.ResolveAsync(bind, config, chanType, sessionId, adminOnly: true);
        if (access.Hint != null) return access.Hint;
        var factoryId = context.RequireFactoryId(nameof(QueryRiskBrief));
        if (access.User != null && access.User.FactoryId != factoryId)
            throw ThrowHelper.BizUser("绑定账号不属于当前工厂，请重新绑定");
        var data = await service.QueryAsync(factoryId, period, topN, cancellationToken);
        return JsonSerializer.Serialize(new { code = 0, msg = "ok", data },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}
