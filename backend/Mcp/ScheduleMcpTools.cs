using System.ComponentModel;
using System.Text.Json;
using ahu.MicrosoftMes.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ahu.MicrosoftMes.Mcp;

[McpServerToolType]
public class ScheduleMcpTools
{
    private readonly IScheduleScoreService _scheduleService;
    private readonly IMcpRequestContext _mcpCtx;
    private readonly IMcpWxBindService _wxBind;
    private readonly IConfiguration _config;

    public ScheduleMcpTools(
        IScheduleScoreService scheduleService,
        IMcpAuthService authService,
        IMcpRequestContext mcpCtx,
        IMcpWxBindService wxBind,
        IConfiguration config)
    {
        _scheduleService = scheduleService;
        _mcpCtx = mcpCtx;
        _wxBind = wxBind;
        _config = config;
    }

    [McpServerTool, Description("计算工单排产优先级。交期紧迫度自动算；客户/金额/换型/齐套四维可手填（不接金蝶时用中性分）。返回加权总分、排名、建议开工顺序。演示模式可空授权码；正式模式仅本厂已绑定管理员可用，不需金蝶授权码。")]
    public async Task<string> CalcSchedulePriority(
        [Description("兼容旧调用保留的参数；车间查询不使用金蝶授权码")] string? authToken = null,
        [Description("可选：按工单号手填四维分，JSON 如 {\"MO001\":{\"customer\":80,\"amount\":60,\"kit\":90,\"changeover\":70}}")] string? manualScores = null,
        [Description("可选：临时覆盖权重，JSON 如 {\"due\":0.3,\"kit\":0.25,\"customer\":0.2,\"changeover\":0.15,\"amount\":0.1}")] string? weights = null,
        [Description("渠道，可空")] string? chanType = null,
        [Description("会话身份，可空")] string? sessionId = null)
    {
        var access = await McpDataAccess.ResolveAsync(_wxBind, _config, chanType, sessionId, adminOnly: true);
        if (access.Hint != null) return access.Hint;

        long factoryId = _mcpCtx.RequireFactoryId(nameof(CalcSchedulePriority));

        var manual = ParseManual(manualScores);
        var w = ParseWeights(weights) ?? new ScheduleWeights();

        var rows = await _scheduleService.ScoreAsync(factoryId, manual, w);

        return JsonSerializer.Serialize(new
        {
            code = 0,
            data = new
            {
                list = rows,
                weights = new
                {
                    due = w.Due,
                    kit = w.Kit,
                    customer = w.Customer,
                    changeover = w.Changeover,
                    amount = w.Amount
                }
            }
        });
    }

    private static Dictionary<string, ScheduleDimScores>? ParseManual(string? manualScores)
    {
        if (string.IsNullOrWhiteSpace(manualScores)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, ScheduleDimScores>>(manualScores,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            throw new McpException($"{nameof(CalcSchedulePriority)}：manualScores 不是合法 JSON：{ex.Message}");
        }
    }

    private static ScheduleWeights? ParseWeights(string? weights)
    {
        if (string.IsNullOrWhiteSpace(weights)) return null;
        try
        {
            return JsonSerializer.Deserialize<ScheduleWeights>(weights,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            throw new McpException($"{nameof(CalcSchedulePriority)}：weights 不是合法 JSON：{ex.Message}");
        }
    }
}
