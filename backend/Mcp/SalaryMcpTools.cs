using System.ComponentModel;
using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ahu.MicrosoftMes.Mcp;

[McpServerToolType]
public class SalaryMcpTools
{
    private readonly ISalaryService _salaryService;
    private readonly IMcpRequestContext _mcpCtx;
    private readonly IMcpWxBindService _wxBind;
    private readonly IConfiguration _config;

    public SalaryMcpTools(
        ISalaryService salaryService,
        IMcpAuthService authService,
        IMcpRequestContext mcpCtx,
        IMcpWxBindService wxBind,
        IConfiguration config)
    {
        _salaryService = salaryService;
        _mcpCtx = mcpCtx;
        _wxBind = wxBind;
        _config = config;
    }

    [McpServerTool, Description("查询工资单列表（财务敏感）。工资由系统计算（工价表在系统内维护，非来自金蝶），本工具只读查询。正式模式钥匙定厂并先绑定：管理员查本厂，生产人员/班组长仅查本人已生成工资单，不是工资预估；不需金蝶授权码。可按周期类型、周期值、状态筛选。")]
    public async Task<string> QuerySalary(
        [Description("兼容旧调用保留的参数；车间查询不使用金蝶授权码")] string? authToken = null,
        [Description("周期类型：1月 2周，可空")] byte? periodType = null,
        [Description("周期值：月为 yyyy-MM，周为 yyyy-Www，可空")] string? periodValue = null,
        [Description("状态：0草稿 1已确认，可空")] byte? status = null,
        [Description("渠道，可空")] string? chanType = null,
        [Description("会话身份，可空")] string? sessionId = null)
    {
        var access = await McpDataAccess.ResolveAsync(_wxBind, _config, chanType, sessionId);
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
            return JsonSerializer.Serialize(new { code = result.Code, msg = result.Msg });

        return JsonSerializer.Serialize(new
        {
            code = 0,
            total = result.Data?.Total ?? 0,
            list = result.Data?.List ?? new List<SalaryListDto>()
        });
    }
}
