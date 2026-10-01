using System.ComponentModel;
using System.Text.Json;
using ahu.MicrosoftMes.Services;
using ModelContextProtocol.Server;

namespace ahu.MicrosoftMes.Mcp;

[McpServerToolType]
public class ReportMcpTools
{
    private readonly IReportService _reportService;
    private readonly IMcpAuthService _authService;
    private readonly IMcpRequestContext _mcpCtx;
    private readonly IMcpWxBindService _wxBind;
    private readonly IConfiguration _config;

    public ReportMcpTools(
        IReportService reportService,
        IMcpAuthService authService,
        IMcpRequestContext mcpCtx,
        IMcpWxBindService wxBind,
        IConfiguration config)
    {
        _reportService = reportService;
        _authService = authService;
        _mcpCtx = mcpCtx;
        _wxBind = wxBind;
        _config = config;
    }

    [McpServerTool, Description("查询报工记录，可按工单号、产品编号、产品名称、复核状态筛选。演示模式下无需授权码；非演示模式需先手机号绑定。")]
    public async Task<string> QueryReport(
        [Description("授权码，由发起授权后获得；演示模式下可留空")] string? authToken = null,
        [Description("工单号，可空")] string? orderNo = null,
        [Description("产品编号，可空")] string? productCode = null,
        [Description("产品名称，可空")] string? productName = null,
        [Description("复核状态：0待复核 1已通过 2已退回，可空查全部")] byte? reviewStatus = null,
        [Description("渠道，可空")] string? chanType = null,
        [Description("会话身份，可空")] string? sessionId = null)
    {
        var bindHint = await _wxBind.RequireBoundOrHintAsync(chanType, sessionId, nameof(QueryReport));
        if (bindHint != null) return bindHint;
        if (!_config.GetValue("Mcp:DemoSkipAuth", false))
            await _authService.RequireAuthAsync(authToken, nameof(QueryReport));

        long factoryId = _mcpCtx.RequireFactoryId(nameof(QueryReport));

        var query = new ReportQueryDto
        {
            OrderNo = orderNo,
            ProductCode = productCode,
            ProductName = productName,
            ReviewStatus = reviewStatus,
            Page = 1,
            PageSize = 20
        };

        var result = await _reportService.QueryAsync(query, factoryId);
        if (result.Code != 0)
            return JsonSerializer.Serialize(new { code = result.Code, msg = result.Msg });

        return JsonSerializer.Serialize(new
        {
            code = 0,
            total = result.Data?.Total ?? 0,
            list = result.Data?.List ?? new List<ReportListDto>()
        });
    }
}
