using System.ComponentModel;
using System.Text.Json;
using ahu.MicrosoftMes.Services;
using ModelContextProtocol.Server;

namespace ahu.MicrosoftMes.Mcp;

[McpServerToolType]
public class ReportMcpTools
{
    private readonly IReportService _reportService;
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
        _mcpCtx = mcpCtx;
        _wxBind = wxBind;
        _config = config;
    }

    [McpServerTool, Description("查询报工记录，管理员查本厂，生产人员/班组长仅查归属于本人的记录；可按工单号、产品编号、产品名称、复核状态筛选。演示模式下无需授权码；正式模式需钥匙定厂及手机号绑定，不需金蝶授权码。")]
    public async Task<string> QueryReport(
        [Description("兼容旧调用保留的参数；车间查询不使用金蝶授权码")] string? authToken = null,
        [Description("工单号，可空")] string? orderNo = null,
        [Description("产品编号，可空")] string? productCode = null,
        [Description("产品名称，可空")] string? productName = null,
        [Description("复核状态：0待复核 1已通过 2已退回，可空查全部")] byte? reviewStatus = null,
        [Description("渠道，可空")] string? chanType = null,
        [Description("会话身份，可空")] string? sessionId = null)
    {
        var access = await McpDataAccess.ResolveAsync(_wxBind, _config, chanType, sessionId);
        if (access.Hint != null) return access.Hint;

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

        var result = await _reportService.QueryAsync(query, factoryId, access.User?.Role is 2 or 3 ? access.User.Id : null);
        if (result.Code != 0)
            return JsonSerializer.Serialize(new { code = result.Code, msg = result.Msg });

        if (access.User?.Role is 2 or 3)
            return JsonSerializer.Serialize(new
            {
                code = 0, total = result.Data?.Total ?? 0,
                list = (result.Data?.List ?? new List<ReportListDto>()).Select(r => new
                { r.Id, r.OperationId, r.OrderNo, r.ProductName, r.OperationName,
                  r.GoodQty, r.DefectQty, r.DefectId, r.DefectName, r.DurationMinutes,
                  r.ReviewStatus, r.RejectReason, r.ReportTime } )
            });

        return JsonSerializer.Serialize(new
        {
            code = 0,
            total = result.Data?.Total ?? 0,
            list = result.Data?.List ?? new List<ReportListDto>()
        });
    }
}
