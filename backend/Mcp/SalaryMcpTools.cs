using System.ComponentModel;
using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;
using ModelContextProtocol.Server;

namespace ahu.MicrosoftMes.Mcp;

[McpServerToolType]
public class SalaryMcpTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ISalaryService _salaryService;
    private readonly ILicenseTierService _license;
    private readonly IMcpRequestContext _mcpCtx;
    private readonly IMcpWxBindService _wxBind;
    private readonly IConfiguration _config;

    public SalaryMcpTools(
        ISalaryService salaryService,
        ILicenseTierService license,
        IMcpAuthService authService,
        IMcpRequestContext mcpCtx,
        IMcpWxBindService wxBind,
        IConfiguration config)
    {
        _salaryService = salaryService;
        _license = license;
        _mcpCtx = mcpCtx;
        _wxBind = wxBind;
        _config = config;
    }

    [McpServerTool, Description("查询工资单列表（财务敏感）。工资由系统计算（工价表在系统内维护，非来自金蝶），本工具只读查询已生成工资单，不是预估。正式模式钥匙定厂并先绑定：管理员查本厂，生产人员/班组长仅查本人；不需金蝶授权码。可按周期类型、周期值、状态筛选。返回 list 含每单扣款，另含 summary（已确认/草稿计件额+返工扣+应发），回答可拼「本月X=已确认Y+待确认Z−返工扣W」。问「今天工资」请用 query_daily_piecework。")]
    public async Task<string> QuerySalary(
        [Description("兼容旧调用保留的参数；车间查询不使用金蝶授权码")] string? authToken = null,
        [Description("周期类型：1月 2周，可空")] byte? periodType = null,
        [Description("周期值：月为 yyyy-MM，周为 yyyy-Www，可空")] string? periodValue = null,
        [Description("状态：0草稿 1已确认，可空")] byte? status = null,
        [Description("渠道，可空")] string? chanType = null,
        [Description("会话身份，可空")] string? sessionId = null,
        [Description("本人网页登录验证的只读查询凭证，不是报工凭证")] string? queryAccessToken = null)
    {
        var access = await McpDataAccess.ResolveAsync(_wxBind, _config, chanType, sessionId, queryAccessToken: queryAccessToken);
        if (access.Hint != null) return access.Hint;

        long factoryId = _mcpCtx.RequireFactoryId(nameof(QuerySalary));

        var query = new SalaryQueryDto
        {
            UserId = access.User?.Role is 2 or 3 ? access.User.Id : null,
            PeriodType = periodType,
            PeriodValue = periodValue,
            Status = status,
            Page = 1,
            PageSize = 20
        };

        var result = await _salaryService.QueryAsync(query, factoryId);
        if (result.Code != 0)
            return JsonSerializer.Serialize(new { code = result.Code, msg = result.Msg }, JsonOpts);

        var summaryResult = await _salaryService.SummarizePeriodAsync(query, factoryId);
        if (summaryResult.Code != 0)
            return JsonSerializer.Serialize(new { code = summaryResult.Code, msg = summaryResult.Msg }, JsonOpts);

        return JsonSerializer.Serialize(new
        {
            code = 0,
            total = result.Data?.Total ?? 0,
            list = result.Data?.List ?? new List<SalaryListDto>(),
            summary = summaryResult.Data,
            formulaHint = "本月应发 = summary.confirmedAmount + summary.draftAmount - summary.deductAmount（即 summary.payableAmount）"
        }, JsonOpts);
    }

    [McpServerTool(Name = "query_daily_piecework"),
     Description("当日计件试算（只读、不落库、不生成工资单）。本人+业务时区当天报工，服务端按报工锁定的工序价目表快照逐条算，合计=Σ(amount−deduct)；拆分已复核/未复核。口径：按报工快照试算、未复核未入账、复核后定。问「这个月工资」请用 query_salary。生产人员/班组长仅本人；需先绑定。")]
    public async Task<string> QueryDailyPiecework(
        [Description("兼容旧调用保留的参数；车间查询不使用金蝶授权码")] string? authToken = null,
        [Description("渠道，可空")] string? chanType = null,
        [Description("会话身份，可空")] string? sessionId = null,
        [Description("本人网页登录验证的只读查询凭证，不是报工凭证")] string? queryAccessToken = null)
    {
        var access = await McpDataAccess.ResolveAsync(_wxBind, _config, chanType, sessionId, queryAccessToken: queryAccessToken);
        if (access.Hint != null) return access.Hint;

        long factoryId = _mcpCtx.RequireFactoryId(nameof(QueryDailyPiecework));

        if (access.User == null)
        {
            return JsonSerializer.Serialize(new
            {
                code = 1,
                msg = "当日计件试算需绑定本人账号后再问「今天工资」"
            }, JsonOpts);
        }

        // role=2/3 仅本人；管理员也只算绑定账号本人（演示问法是「我今天」）
        var result = await _salaryService.EstimateDailyPieceworkAsync(
            factoryId, access.User.Id, _config.GetValue<string>("Ai:BusinessTimeZone"));
        if (result.Code != 0)
            return JsonSerializer.Serialize(new { code = result.Code, msg = result.Msg }, JsonOpts);

        var data = result.Data!;
        if (data.ReportCount == 0)
        {
            return JsonSerializer.Serialize(new
            {
                code = 0,
                msg = $"今日（{data.BusinessDate}）暂无报工，无法试算",
                data
            }, JsonOpts);
        }

        if (data.MatchedCount == 0)
        {
            return JsonSerializer.Serialize(new
            {
                code = 0,
                msg = $"今日有 {data.ReportCount} 条报工，但未匹配到工价，不编造金额",
                data
            }, JsonOpts);
        }

        return JsonSerializer.Serialize(new
        {
            code = 0,
            msg = "ok",
            data
        }, JsonOpts);
    }

    [McpServerTool(Name = "get_work_order_payable"),
     Description("按工单查累计应付工资（docs/420/421，只读）。口语「计件工资」= payableTotal（计件+计时+固定）。仅管理员；需计件工资许可。orderId 优先于 orderNo。")]
    public async Task<string> GetWorkOrderPayable(
        [Description("工单号，与 orderId 至少一个")] string? orderNo = null,
        [Description("工单ID，优先")] long? orderId = null,
        [Description("渠道，可空")] string? chanType = null,
        [Description("会话身份，可空")] string? sessionId = null,
        [Description("本人网页登录验证的只读查询凭证，不是报工凭证")] string? queryAccessToken = null)
    {
        var access = await McpDataAccess.ResolveAsync(_wxBind, _config, chanType, sessionId, adminOnly: true, queryAccessToken: queryAccessToken);
        if (access.Hint != null) return access.Hint;

        long factoryId = _mcpCtx.RequireFactoryId(nameof(GetWorkOrderPayable));
        try
        {
            await _license.EnsureFeatureAsync(factoryId, LicenseFeature.PieceWage);
            var result = await _salaryService.WorkOrderSummaryAsync(orderId, orderNo, factoryId);
            return JsonSerializer.Serialize(new { code = result.Code, msg = result.Msg, data = result.Data }, JsonOpts);
        }
        catch (BusinessException ex)
        {
            return JsonSerializer.Serialize(new { code = ex.Code == 0 ? 1 : ex.Code, msg = ex.Message }, JsonOpts);
        }
    }

    [McpServerTool(Name = "get_completed_work_orders_payable"),
     Description("已完工工单应付台账与总合计（docs/420/421，只读）。问「完成工单要付多少」用本工具。仅管理员；需计件工资许可。")]
    public async Task<string> GetCompletedWorkOrdersPayable(
        [Description("渠道，可空")] string? chanType = null,
        [Description("会话身份，可空")] string? sessionId = null,
        [Description("本人网页登录验证的只读查询凭证，不是报工凭证")] string? queryAccessToken = null)
    {
        var access = await McpDataAccess.ResolveAsync(_wxBind, _config, chanType, sessionId, adminOnly: true, queryAccessToken: queryAccessToken);
        if (access.Hint != null) return access.Hint;

        long factoryId = _mcpCtx.RequireFactoryId(nameof(GetCompletedWorkOrdersPayable));
        try
        {
            await _license.EnsureFeatureAsync(factoryId, LicenseFeature.PieceWage);
            var result = await _salaryService.CompletedWorkOrdersAsync(factoryId);
            return JsonSerializer.Serialize(new { code = result.Code, msg = result.Msg, data = result.Data }, JsonOpts);
        }
        catch (BusinessException ex)
        {
            return JsonSerializer.Serialize(new { code = ex.Code == 0 ? 1 : ex.Code, msg = ex.Message }, JsonOpts);
        }
    }
}
