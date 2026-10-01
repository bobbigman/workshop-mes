using System.ComponentModel;
using System.Text.Json;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Services;
using ModelContextProtocol.Server;

namespace ahu.MicrosoftMes.Mcp;

/// <summary>
/// MCP 工具：工单列表 + 单张详进度。复用 IWorkOrderService / WorkOrderProgressCalculator。
/// </summary>
[McpServerToolType]
public class WorkOrderMcpTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IWorkOrderService _workOrderService;
    private readonly IMcpAuthService _authService;
    private readonly IMcpRequestContext _mcpCtx;
    private readonly IMcpWxBindService _wxBind;
    private readonly IConfiguration _config;

    public WorkOrderMcpTools(
        IWorkOrderService workOrderService,
        IMcpAuthService authService,
        IMcpRequestContext mcpCtx,
        IMcpWxBindService wxBind,
        IConfiguration config)
    {
        _workOrderService = workOrderService;
        _authService = authService;
        _mcpCtx = mcpCtx;
        _wxBind = wxBind;
        _config = config;
    }

    [McpServerTool, Description("查询车间工单列表，可按关键词（工单号/产品名/产品编号）或状态筛选。演示模式下无需授权码；非演示模式需先手机号绑定。查「列表/今天有哪些单」用本工具；查「某单做到哪了/进度」请用 query_work_order_progress。")]
    public async Task<string> QueryWorkOrder(
        [Description("授权码，由发起授权后获得；演示模式下可留空")] string? authToken = null,
        [Description("关键词：工单号、产品名称或产品编号，可空")] string? keyword = null,
        [Description("状态：0未开始 1执行中 2已完成 3已取消，可空查全部")] byte? status = null,
        [Description("渠道，可空")] string? chanType = null,
        [Description("会话身份，可空")] string? sessionId = null)
    {
        var bindHint = await _wxBind.RequireBoundOrHintAsync(chanType, sessionId, nameof(QueryWorkOrder));
        if (bindHint != null) return bindHint;
        if (!_config.GetValue("Mcp:DemoSkipAuth", false))
            await _authService.RequireAuthAsync(authToken, nameof(QueryWorkOrder));

        long factoryId = _mcpCtx.RequireFactoryId(nameof(QueryWorkOrder));

        var query = new WorkOrderQueryDto
        {
            Keyword = keyword,
            Status = status,
            Page = 1,
            PageSize = 20
        };

        var result = await _workOrderService.QueryAsync(query, factoryId);
        if (result.Code != 0)
            return JsonSerializer.Serialize(new { code = result.Code, msg = result.Msg }, JsonOpts);

        return JsonSerializer.Serialize(new
        {
            code = 0,
            total = result.Data?.Total ?? 0,
            list = result.Data?.List ?? new List<WorkOrderListDto>()
        }, JsonOpts);
    }

    [McpServerTool, Description("查询单张工单生产进度（做到哪了/第几道/完成百分比/距交期）。按工单号或产品编号/名称定位；演示模式可空授权码。查列表请用 query_work_order，不要用本工具顶替。")]
    public async Task<string> QueryWorkOrderProgress(
        [Description("授权码，由发起授权后获得；演示模式下可留空")] string? authToken = null,
        [Description("工单号（精确，推荐）")] string? orderNo = null,
        [Description("产品编号（精确）")] string? productCode = null,
        [Description("产品名称（模糊）")] string? productName = null,
        [Description("工序名称（可选；只筛选展示的工序，整单完成数仍按全工序计算）")] string? operationName = null,
        [Description("渠道，可空")] string? chanType = null,
        [Description("会话身份，可空")] string? sessionId = null)
    {
        var bindHint = await _wxBind.RequireBoundOrHintAsync(chanType, sessionId, nameof(QueryWorkOrderProgress));
        if (bindHint != null) return bindHint;
        if (!_config.GetValue("Mcp:DemoSkipAuth", false))
            await _authService.RequireAuthAsync(authToken, nameof(QueryWorkOrderProgress));

        long factoryId = _mcpCtx.RequireFactoryId(nameof(QueryWorkOrderProgress));
        var no = (orderNo ?? "").Trim();
        var code = (productCode ?? "").Trim();
        var name = (productName ?? "").Trim();
        var opName = (operationName ?? "").Trim();

        if (no.Length == 0 && code.Length == 0 && name.Length == 0)
            throw ThrowHelper.BizUser("请给工单号或产品编号/名称");

        if (no.Length > 0)
        {
            var byNo = await FindOpenOrAnyByOrderNoAsync(no, factoryId);
            if (byNo == null)
                return JsonSerializer.Serialize(new { found = false, msg = "未找到该单" }, JsonOpts);

            if (code.Length > 0 && !string.Equals(byNo.ProductCode, code, StringComparison.OrdinalIgnoreCase))
                throw ThrowHelper.BizUser("单号与产品不一致");
            if (name.Length > 0 && byNo.ProductName.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)
                throw ThrowHelper.BizUser("单号与产品不一致");

            var detail = await _workOrderService.GetAsync(byNo.Id, factoryId);
            if (detail.Code != 0 || detail.Data == null)
                return JsonSerializer.Serialize(new { found = false, msg = detail.Msg ?? "未找到该单" }, JsonOpts);

            return JsonSerializer.Serialize(BuildProgressPayload(detail.Data, opName), JsonOpts);
        }

        if (code.Length > 0 && name.Length > 0)
        {
            var byCode = await FindIncompleteByProductAsync(code, null, factoryId);
            var byName = await FindIncompleteByProductAsync(null, name, factoryId);
            var codeProductIds = byCode.Select(x => x.ProductCode).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var nameProductIds = byName.Select(x => x.ProductCode).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (codeProductIds.Count > 0 && nameProductIds.Count > 0 && !codeProductIds.Overlaps(nameProductIds))
                throw ThrowHelper.BizUser("产品编号与名称指向不同产品，请只给一个条件或核对后重试");
        }

        var candidates = await FindIncompleteByProductAsync(
            code.Length > 0 ? code : null,
            name.Length > 0 ? name : null,
            factoryId);

        if (candidates.Count == 0)
            return JsonSerializer.Serialize(new { found = false, msg = "未找到相关工单" }, JsonOpts);

        if (candidates.Count > 1)
        {
            return JsonSerializer.Serialize(new
            {
                found = false,
                msg = "匹配到多张未完成工单，请给具体工单号",
                candidates = candidates.Take(10).Select(c => new
                {
                    orderNo = c.OrderNo,
                    productCode = c.ProductCode,
                    productName = c.ProductName,
                    status = c.Status,
                    dueDate = AiBusinessTime.FormatDate(c.DueDate)
                })
            }, JsonOpts);
        }

        var one = await _workOrderService.GetAsync(candidates[0].Id, factoryId);
        if (one.Code != 0 || one.Data == null)
            return JsonSerializer.Serialize(new { found = false, msg = one.Msg ?? "未找到相关工单" }, JsonOpts);

        return JsonSerializer.Serialize(BuildProgressPayload(one.Data, opName), JsonOpts);
    }

    private async Task<WorkOrderListDto?> FindOpenOrAnyByOrderNoAsync(string orderNo, long factoryId)
    {
        var result = await _workOrderService.QueryAsync(new WorkOrderQueryDto
        {
            Keyword = orderNo,
            Page = 1,
            PageSize = 50
        }, factoryId);
        if (result.Code != 0 || result.Data?.List == null)
            return null;
        return result.Data.List.FirstOrDefault(x =>
            string.Equals(x.OrderNo, orderNo, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<List<WorkOrderListDto>> FindIncompleteByProductAsync(
        string? productCode, string? productName, long factoryId)
    {
        var keyword = !string.IsNullOrEmpty(productCode) ? productCode
            : (!string.IsNullOrEmpty(productName) ? productName : "");
        var result = await _workOrderService.QueryAsync(new WorkOrderQueryDto
        {
            Keyword = keyword,
            Page = 1,
            PageSize = 50,
            ExcludeCancelled = true
        }, factoryId);
        if (result.Code != 0 || result.Data?.List == null)
            return new List<WorkOrderListDto>();

        IEnumerable<WorkOrderListDto> q = result.Data.List.Where(x => x.Status is 0 or 1);
        if (!string.IsNullOrEmpty(productCode))
            q = q.Where(x => string.Equals(x.ProductCode, productCode, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(productName))
            q = q.Where(x => x.ProductName.IndexOf(productName, StringComparison.OrdinalIgnoreCase) >= 0);
        return q.ToList();
    }

    private object BuildProgressPayload(WorkOrderDetailDto d, string operationNameFilter)
    {
        var opInputs = d.Tasks.Select(t => new WorkOrderProgressCalculator.OpQty
        {
            OperationId = t.OperationId,
            PlanQty = t.PlanQty,
            DoneQty = t.DoneQty,
            DefectQty = t.DefectQty
        }).ToList();
        var prog = WorkOrderProgressCalculator.Calc(opInputs, d.Qty, nameof(QueryWorkOrderProgress));

        var tasks = d.Tasks.AsEnumerable();
        if (!string.IsNullOrEmpty(operationNameFilter))
            tasks = tasks.Where(t => t.OperationName.IndexOf(operationNameFilter, StringComparison.OrdinalIgnoreCase) >= 0);

        var tz = AiBusinessTime.Resolve(_config.GetValue<string>("Ai:BusinessTimeZone"));
        var today = AiBusinessTime.TodayStart(tz);
        int? daysToDue = null;
        if (d.DueDate != null)
        {
            var dueDay = d.DueDate.Value.Date;
            daysToDue = (int)(dueDay - today.Date).TotalDays;
        }

        return new
        {
            found = true,
            orderNo = d.OrderNo,
            productCode = d.ProductCode,
            productName = d.ProductName,
            qty = d.Qty,
            doneQty = prog.DoneQty,
            remainQty = prog.RemainQty,
            progressPercent = prog.ProgressPercent,
            progressHint = prog.ProgressHint,
            status = d.Status,
            dueDate = AiBusinessTime.FormatDate(d.DueDate),
            daysToDue,
            tasks = tasks.Select(t => new
            {
                seq = t.Seq,
                operationName = t.OperationName,
                planQty = t.PlanQty,
                doneQty = t.DoneQty,
                defectQty = t.DefectQty,
                opStatus = WorkOrderProgressCalculator.OpStatus(t.PlanQty, t.DoneQty, t.DefectQty)
            })
        };
    }
}
