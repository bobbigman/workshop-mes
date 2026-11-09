using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IWorkOrderService
{
    Task<ApiResult<PageResult<WorkOrderListDto>>> QueryAsync(WorkOrderQueryDto query, long factoryId);
    Task<ApiResult<WorkOrderStatusCountsDto>> GetStatusCountsAsync(long factoryId);
    Task<ApiResult<WorkOrderDetailDto>> GetAsync(long id, long factoryId);
    Task<ApiResult<object?>> CreateAsync(WorkOrderCreateDto dto, long currentUserId, long factoryId);
    Task<ApiResult<object?>> UpdateAsync(long id, WorkOrderUpdateDto dto, long currentUserId, long factoryId);
    Task<ApiResult<object?>> TransitionAsync(long id, string action, long factoryId);
    Task<ApiResult<object?>> CopyAsync(long id, long currentUserId, long factoryId);
    Task<ApiResult<object?>> DeleteAsync(long id);
    Task<ApiResult<string>> GetQrContentAsync(long id, long factoryId);
    /// <param name="reportPermissionUserId">非空时按该用户报工权限过滤 tasks；空则不过滤（PC/AI 查进度用）。</param>
    Task<ApiResult<WorkOrderDetailDto>> GetByOrderNoAsync(
        string orderNo, long factoryId, long? reportPermissionUserId = null, byte? reportPermissionRole = null);
}

public class WorkOrderQueryDto
{
    public string? Keyword { get; set; }
    public byte? Status { get; set; }
    public bool ExcludeCancelled { get; set; }  // 「全部」/H5：排除已作废(3)、已挂起(6)及草稿/待审
    /// <summary>为 true 时只返回未结束工单（status 非 2 非 3），供报工下拉。</summary>
    public bool ExcludeFinished { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    /// <summary>可选；须为本厂 work_order 字段。值空白时不筛，但仍校验 ID。</summary>
    public long? CustomFieldId { get; set; }
    public string? CustomFieldValue { get; set; }
    /// <summary>exact | contains；默认 exact。单选/数字仅 exact。兼容旧端；优先用 FilterField。</summary>
    public string? CustomFieldMatch { get; set; }

    /// <summary>
    /// 快捷过滤字段：keyword | orderNo | productCode | productName | qty | dueDate | cf_{id}。
    /// 有值时优先于 Keyword + CustomField*。
    /// </summary>
    public string? FilterField { get; set; }
    /// <summary>eq | contains | gt | lt。默认：文本 contains，其它 eq。</summary>
    public string? FilterOp { get; set; }
    public string? FilterValue { get; set; }
}

public class WorkOrderStatusCountsDto
{
    public int All { get; set; }
    public int NotStarted { get; set; }
    public int Doing { get; set; }
    public int Done { get; set; }
    public int Cancelled { get; set; }
    /// <summary>docs/425：草稿(status=4)</summary>
    public int Draft { get; set; }
    /// <summary>docs/435：待审核(status=5)</summary>
    public int PendingAudit { get; set; }
    /// <summary>docs/437：已挂起(status=6)</summary>
    public int Suspended { get; set; }
}

public class WorkOrderCreateDto
{
    public Dictionary<long, Dictionary<long, string?>>? OperationAttributes { get; set; }
    /// <summary>工序→预览指纹（quoteToken）；服务端据此校验价目表/属性未变化后锁价。</summary>
    public Dictionary<long, string>? QuoteTokens { get; set; }
    public long? CopyFromId { get; set; }
    public string? OrderNo { get; set; }             // 可自定义，空则自动生成
    public long ProductId { get; set; }
    public int Qty { get; set; }
    public DateTime? DueDate { get; set; }           // 计划交期，可空
    public Dictionary<long, string> Ext { get; set; } = new();
    /// <summary>兼容旧客户端：true=draft；false=approve。优先看 SaveAction。</summary>
    public bool AsDraft { get; set; }
    /// <summary>docs/435：draft=暂存(4)；pending=保存待审(5)不锁价；approve=审核锁价→未开始(0)。</summary>
    public string? SaveAction { get; set; }
}

public class WorkOrderPlanDto
{
    public long OperationId { get; set; }
    public int PlanQty { get; set; }
}

public class WorkOrderUpdateDto
{
    public Dictionary<long, Dictionary<long, string?>>? OperationAttributes { get; set; }
    /// <summary>工序→预览指纹（quoteToken）；服务端据此校验价目表/属性未变化后锁价。</summary>
    public Dictionary<long, string>? QuoteTokens { get; set; }
    public string? OrderNo { get; set; }             // 可改
    public long? ProductId { get; set; }             // 可换
    public int? Qty { get; set; }                    // 可改
    public DateTime? DueDate { get; set; }           // 计划交期，可空
    public List<WorkOrderPlanDto>? PlanQty { get; set; } // 工序计划数
    public Dictionary<long, string>? Ext { get; set; }   // 自定义字段
    /// <summary>兼容旧客户端：true=draft；false=approve。优先看 SaveAction。</summary>
    public bool AsDraft { get; set; }
    /// <summary>docs/435：draft / pending / approve。已生效(0/1)仅允许 approve 语义（锁价校验）。</summary>
    public string? SaveAction { get; set; }
}

public class WorkOrderListOpDto
{
    public string OperationName { get; set; } = "";
    public int PlanQty { get; set; }
    public int DoneQty { get; set; }
}

public class WorkOrderListDto
{
    public long Id { get; set; }
    public string OrderNo { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int Qty { get; set; }
    public int PlanQty { get; set; }
    public int DoneQty { get; set; }
    public int RemainQty { get; set; }
    /// <summary>工序达成进度 0～100；无有效正计划工序时为 null。</summary>
    public int? ProgressPercent { get; set; }
    /// <summary>progressPercent 为空时的说明（无工序计划 / 暂无有效计划）。</summary>
    public string? ProgressHint { get; set; }
    public byte Status { get; set; }
    /// <summary>docs/422：已结束撤回重开；进度满仍执行中时前端提示「已重新打开」。</summary>
    public bool Reopened { get; set; }
    public DateTime? DueDate { get; set; }
    public string DueState { get; set; } = "";       // normal/warning/overdue
    public DateTime CreatedAt { get; set; }
    public List<WorkOrderListOpDto> Ops { get; set; } = new();
    /// <summary>仅含本厂已开启「在工单列表显示」的工单字段；键为字段 ID。</summary>
    public Dictionary<long, string> Ext { get; set; } = new();
}

public class WorkOrderTaskDto
{
    public Dictionary<long, string?> OperationAttributes { get; set; } = new();
    public bool HasReports { get; set; }
    public ProcessingQuote? ProcessingPrice { get; set; }
    public long Id { get; set; }               // prod_work_order_operation.id；旧数据兜底临时任务为 0
    public long OperationId { get; set; }
    public string OperationName { get; set; } = "";
    public int Seq { get; set; }
    public int PlanQty { get; set; }
    public int DoneQty { get; set; }       // 该工序已报良品合计
    public int DefectQty { get; set; }     // 该工序已报不良合计
    public long? AssigneeUserId { get; set; }   // 冗余首个执行人；空=未派工
    public string AssigneeName { get; set; } = "";
    /// <summary>多人派工列表（docs/202）；空=未派工。</summary>
    public List<WorkOrderAssigneeDto> Assignees { get; set; } = new();
    /// <summary>派工班组=部门（docs/214）；与人互斥。</summary>
    public long? AssigneeDepartmentId { get; set; }
    public string AssigneeDepartmentName { get; set; } = "";
}

public class WorkOrderAssigneeDto
{
    public long UserId { get; set; }
    public string UserName { get; set; } = "";
}

public class WorkOrderDetailDto
{
    public long Id { get; set; }
    public string OrderNo { get; set; } = "";
    public long ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int Qty { get; set; }
    public byte Status { get; set; }
    /// <summary>docs/422：已结束撤回重开。</summary>
    public bool Reopened { get; set; }
    public DateTime? DueDate { get; set; }
    public string DueState { get; set; } = "";       // normal/warning/overdue
    /// <summary>docs/436：创建人姓名；空则前端显示 —</summary>
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    /// <summary>docs/436：审核人姓名；未审为空</summary>
    public string AuditedByName { get; set; } = "";
    public DateTime? AuditedAt { get; set; }
    public Dictionary<long, string> Ext { get; set; } = new();
    public List<WorkOrderTaskDto> Tasks { get; set; } = new();
}

public class WorkOrderService : IWorkOrderService
{
    private readonly AppDbContext _db;
    private readonly WechatEventService? _events;
    public WorkOrderService(AppDbContext db, WechatEventService? events = null) { _db = db; _events = events; }

    /// <summary>docs/435：4 草稿、5 待审核 —— 未审核不可报工/不进现场列表。</summary>
    public static bool IsPreAuditStatus(byte status) => status is 4 or 5;

    /// <summary>draft|pending|approve；空则回落 asDraft（true→draft，false→approve）。</summary>
    public static string ResolveSaveAction(bool asDraft, string? saveAction)
    {
        var a = (saveAction ?? "").Trim().ToLowerInvariant();
        if (a is "draft" or "pending" or "approve") return a;
        return asDraft ? "draft" : "approve";
    }

    /// <summary>docs/436：仅首次审核写入；已有审核时间则不改。</summary>
    public static void StampFirstAudit(ProdWorkOrder order, long userId)
    {
        if (order.AuditedAt.HasValue) return;
        order.AuditedBy = userId;
        order.AuditedAt = DateTime.Now;
    }

    public async Task<ApiResult<PageResult<WorkOrderListDto>>> QueryAsync(WorkOrderQueryDto query, long factoryId)
    {
        await TenantDataGuard.WorkOrdersAsync(_db, factoryId);
        var q = from o in _db.WorkOrders.AsNoTracking()
                where o.FactoryId == factoryId
                join p in _db.Products.AsNoTracking().Where(p => p.FactoryId == factoryId) on o.ProductId equals p.Id
                select new WorkOrderQueryJoin { Order = o, ProductName = p.Name, ProductCode = p.Code };

        if (query.Status.HasValue)
            q = q.Where(x => x.Order.Status == query.Status.Value);
        else if (query.ExcludeFinished)
            q = q.Where(x => x.Order.Status != 2 && x.Order.Status != 3 && x.Order.Status != 6);
        else if (query.ExcludeCancelled)
            // 「全部」/现场列表：不含已作废、已挂起、草稿、待审核（docs/425/435/437）
            q = q.Where(x => x.Order.Status != 3 && x.Order.Status != 4 && x.Order.Status != 5 && x.Order.Status != 6);

        if (!string.IsNullOrWhiteSpace(query.FilterField))
        {
            q = await ApplyQuickFilterAsync(q, query, factoryId);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(query.Keyword))
                q = q.Where(x => x.Order.OrderNo.Contains(query.Keyword) || x.ProductName.Contains(query.Keyword) || x.ProductCode.Contains(query.Keyword));

            var filter = await ParseCustomFieldFilterAsync(query, factoryId);
            if (filter != null)
            {
                var fieldId = filter.Value.FieldId;
                var value = filter.Value.Value;
                if (filter.Value.Match == "exact")
                {
                    q = q.Where(x => _db.CustomFieldValues.AsNoTracking()
                        .Any(v => v.FieldId == fieldId && v.Value == value && v.TargetId == x.Order.Id));
                }
                else
                {
                    var likePattern = "%" + EscapeLikePattern(value) + "%";
                    q = q.Where(x => _db.CustomFieldValues.AsNoTracking()
                        .Any(v => v.FieldId == fieldId && v.Value != null
                                  && EF.Functions.Like(v.Value, likePattern)
                                  && v.TargetId == x.Order.Id));
                }
            }
        }

        var total = await q.CountAsync();
        var list = await q.OrderByDescending(x => x.Order.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new WorkOrderListDto
            {
                Id = x.Order.Id,
                OrderNo = x.Order.OrderNo,
                ProductCode = x.ProductCode,
                ProductName = x.ProductName,
                Qty = x.Order.Qty,
                PlanQty = x.Order.Qty,
                Status = x.Order.Status,
                Reopened = x.Order.Reopened,
                DueDate = x.Order.DueDate,
                CreatedAt = x.Order.CreatedAt
            }).ToListAsync();

        await FillListProgressAsync(list, factoryId);
        var healed = await HealStaleDoingAsync(list, factoryId);
        if (healed > 0 && query.Status == 1)
        {
            // 本页已纠成已结束的行从「执行中」结果里拿掉，避免页内状态错位
            list.RemoveAll(x => x.Status == 2 && !x.Reopened);
            total = Math.Max(0, total - healed);
        }
        await FillListExtAsync(list, factoryId);

        // 交期三态（列表逐条算；Calc 由 DueStateHelper 实现）
        var now = DateTime.Now;
        foreach (var item in list)
            item.DueState = DueStateHelper.Calc(item.Status, item.DueDate, now);

        return ApiResult<PageResult<WorkOrderListDto>>.Ok(new PageResult<WorkOrderListDto> { List = list, Total = total });
    }

    /// <summary>工单列表联表投影（便于快捷过滤复用）。</summary>
    private sealed class WorkOrderQueryJoin
    {
        public ProdWorkOrder Order { get; set; } = null!;
        public string ProductName { get; set; } = "";
        public string ProductCode { get; set; } = "";
    }

    /// <summary>
    /// 快捷过滤：字段 + 比较符 + 值（对标金蝶列表快捷过滤）。
    /// 值空白：已选字段但不筛。
    /// </summary>
    private async Task<IQueryable<WorkOrderQueryJoin>> ApplyQuickFilterAsync(
        IQueryable<WorkOrderQueryJoin> q, WorkOrderQueryDto query, long factoryId)
    {
        var field = query.FilterField!.Trim();
        var value = string.IsNullOrWhiteSpace(query.FilterValue) ? "" : query.FilterValue.Trim();
        if (value.Length > 256)
            throw ThrowHelper.Biz(nameof(QueryAsync), "筛选值太长，最多 256 个字符，请缩短后重试。");

        var op = string.IsNullOrWhiteSpace(query.FilterOp)
            ? ""
            : query.FilterOp.Trim().ToLowerInvariant();

        // 值空：不应用条件（与旧自定义字段行为一致）
        if (value.Length == 0)
            return q;

        if (field.StartsWith("cf_", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(field.AsSpan(3), out var cfId) && cfId > 0)
        {
            var cfField = await _db.CustomFields.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == cfId && f.FactoryId == factoryId)
                ?? throw ThrowHelper.Biz(nameof(QueryAsync), "所选自定义字段不存在，可能已被删除。请重新选择筛选字段。");
            if (cfField.Target != "work_order")
                throw ThrowHelper.Biz(nameof(QueryAsync), "当前筛选只支持工单级自定义字段。");

            if (string.IsNullOrEmpty(op))
                op = cfField.FieldType == "text" ? "contains" : "eq";
            if (op is not ("eq" or "contains" or "gt" or "lt"))
                throw ThrowHelper.Biz(nameof(QueryAsync), "比较方式只支持：等于、包含、大于、小于。");
            if (op is "gt" or "lt")
                throw ThrowHelper.Biz(nameof(QueryAsync), "自定义字段暂不支持大于/小于，请用「等于」或「包含」。");
            if (cfField.FieldType is "single" or "number" && op == "contains")
                throw ThrowHelper.Biz(nameof(QueryAsync), "单选/数字字段不支持「包含」，请改用「等于」。");

            if (op == "eq")
            {
                return q.Where(x => _db.CustomFieldValues.AsNoTracking()
                    .Any(v => v.FieldId == cfId && v.Value == value && v.TargetId == x.Order.Id));
            }
            var likePattern = "%" + EscapeLikePattern(value) + "%";
            return q.Where(x => _db.CustomFieldValues.AsNoTracking()
                .Any(v => v.FieldId == cfId && v.Value != null
                          && EF.Functions.Like(v.Value, likePattern)
                          && v.TargetId == x.Order.Id));
        }

        if (string.IsNullOrEmpty(op))
            op = field is "qty" or "dueDate" ? "eq" : "contains";
        if (op is not ("eq" or "contains" or "gt" or "lt"))
            throw ThrowHelper.Biz(nameof(QueryAsync), "比较方式只支持：等于、包含、大于、小于。");

        switch (field)
        {
            case "keyword":
                if (op is "gt" or "lt")
                    throw ThrowHelper.Biz(nameof(QueryAsync), "「工单号/产品」只支持等于或包含。");
                if (op == "eq")
                    return q.Where(x => x.Order.OrderNo == value || x.ProductName == value || x.ProductCode == value);
                return q.Where(x => x.Order.OrderNo.Contains(value) || x.ProductName.Contains(value) || x.ProductCode.Contains(value));

            case "orderNo":
                if (op is "gt" or "lt")
                    throw ThrowHelper.Biz(nameof(QueryAsync), "工单号只支持等于或包含。");
                return op == "eq"
                    ? q.Where(x => x.Order.OrderNo == value)
                    : q.Where(x => x.Order.OrderNo.Contains(value));

            case "productCode":
                if (op is "gt" or "lt")
                    throw ThrowHelper.Biz(nameof(QueryAsync), "产品编号只支持等于或包含。");
                return op == "eq"
                    ? q.Where(x => x.ProductCode == value)
                    : q.Where(x => x.ProductCode.Contains(value));

            case "productName":
                if (op is "gt" or "lt")
                    throw ThrowHelper.Biz(nameof(QueryAsync), "产品名称只支持等于或包含。");
                return op == "eq"
                    ? q.Where(x => x.ProductName == value)
                    : q.Where(x => x.ProductName.Contains(value));

            case "qty":
                if (op == "contains")
                    throw ThrowHelper.Biz(nameof(QueryAsync), "数量不支持「包含」，请用等于/大于/小于。");
                if (!int.TryParse(value, out var qty))
                    throw ThrowHelper.Biz(nameof(QueryAsync), "数量请输入整数。");
                return op switch
                {
                    "eq" => q.Where(x => x.Order.Qty == qty),
                    "gt" => q.Where(x => x.Order.Qty > qty),
                    _ => q.Where(x => x.Order.Qty < qty)
                };

            case "dueDate":
                if (op == "contains")
                    throw ThrowHelper.Biz(nameof(QueryAsync), "交期不支持「包含」，请用等于/大于/小于。");
                if (!DateTime.TryParse(value, out var due))
                    throw ThrowHelper.Biz(nameof(QueryAsync), "交期请输入有效日期，例如 2026-11-07。");
                var day = due.Date;
                var next = day.AddDays(1);
                return op switch
                {
                    "eq" => q.Where(x => x.Order.DueDate != null && x.Order.DueDate >= day && x.Order.DueDate < next),
                    "gt" => q.Where(x => x.Order.DueDate != null && x.Order.DueDate >= next),
                    _ => q.Where(x => x.Order.DueDate != null && x.Order.DueDate < day)
                };

            default:
                throw ThrowHelper.Biz(nameof(QueryAsync), $"不支持的筛选字段「{field}」。请重新选择。");
        }
    }

    /// <summary>
    /// 解析自定义字段筛选项。返回 null=不应用筛选。
    /// </summary>
    private async Task<(long FieldId, string Value, string Match)?> ParseCustomFieldFilterAsync(
        WorkOrderQueryDto query, long factoryId)
    {
        var hasId = query.CustomFieldId.HasValue && query.CustomFieldId.Value > 0;
        var value = string.IsNullOrWhiteSpace(query.CustomFieldValue) ? "" : query.CustomFieldValue.Trim();
        if (value.Length > 256)
            throw ThrowHelper.Biz(nameof(QueryAsync), "自定义字段的筛选值太长，最多支持256个字符，请缩短后重试。");

        if (!hasId)
        {
            if (value.Length > 0)
                throw ThrowHelper.Biz(nameof(QueryAsync), "请先选择一个自定义字段作为筛选条件，再输入筛选值查询。");
            return null;
        }

        var fieldId = query.CustomFieldId!.Value;
        var field = await _db.CustomFields.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fieldId && f.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(QueryAsync), "所选自定义字段不存在，可能已被删除。请到「自定义字段管理」确认当前可用的字段列表。");
        if (field.Target != "work_order")
            throw ThrowHelper.Biz(nameof(QueryAsync), "当前筛选只支持工单级别的自定义字段，不支持工序或其他类型字段。请到「自定义字段管理」确认哪些字段属于工单。");

        var match = string.IsNullOrWhiteSpace(query.CustomFieldMatch)
            ? "exact"
            : query.CustomFieldMatch.Trim().ToLowerInvariant();
        if (match is not ("exact" or "contains"))
            throw ThrowHelper.Biz(nameof(QueryAsync), "自定义字段的匹配方式只支持精确匹配和模糊包含两种，请选择其中一种。");

        if ((field.FieldType == "single" || field.FieldType == "number") && match != "exact")
            throw ThrowHelper.Biz(nameof(QueryAsync), "单选字段和数字字段只能精确匹配，不支持模糊搜索。请切换为精确匹配，或改用文本类字段做模糊筛选。");

        // 值空白：已校验字段，不应用筛选
        if (value.Length == 0)
            return null;

        return (fieldId, value, match);
    }

    private static string EscapeLikePattern(string input) =>
        input.Replace("[", "[[]", StringComparison.Ordinal)
             .Replace("%", "[%]", StringComparison.Ordinal)
             .Replace("_", "[_]", StringComparison.Ordinal);

    private async Task FillListExtAsync(List<WorkOrderListDto> list, long factoryId)
    {
        if (list.Count == 0) return;

        var showFieldIds = await _db.CustomFields.AsNoTracking()
            .Where(f => f.FactoryId == factoryId && f.Target == "work_order" && f.ShowInOrderList)
            .OrderBy(f => f.Id)
            .Select(f => f.Id)
            .ToListAsync();
        if (showFieldIds.Count == 0) return;

        var orderIds = list.Select(x => x.Id).ToList();
        var values = await (
            from v in _db.CustomFieldValues.AsNoTracking()
            join f in _db.CustomFields.AsNoTracking() on v.FieldId equals f.Id
            where showFieldIds.Contains(v.FieldId)
                  && orderIds.Contains(v.TargetId)
                  && f.FactoryId == factoryId
                  && f.Target == "work_order"
            select new { v.FieldId, v.TargetId, v.Value }
        ).ToListAsync();

        var map = values.GroupBy(v => v.TargetId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.FieldId, x => x.Value ?? ""));

        foreach (var item in list)
        {
            if (map.TryGetValue(item.Id, out var ext))
                item.Ext = ext;
        }
    }

    /// <summary>
    /// 批量填充列表进度与工序链，避免 N+1；与看板/监控共用 WorkOrderProgressQuery。
    /// </summary>
    private async Task FillListProgressAsync(List<WorkOrderListDto> list, long factoryId)
    {
        if (list.Count == 0) return;

        // 列表项未带 ProductId，再查一次本页工单产品（仅本厂）
        var ids = list.Select(x => x.Id).ToList();
        var keys = await _db.WorkOrders.AsNoTracking()
            .Where(o => o.FactoryId == factoryId && ids.Contains(o.Id))
            .Select(o => new WorkOrderProgressQuery.OrderKey { Id = o.Id, ProductId = o.ProductId, Qty = o.Qty })
            .ToListAsync();

        var bundles = await WorkOrderProgressQuery.LoadAsync(_db, factoryId, keys, nameof(FillListProgressAsync));
        foreach (var item in list)
        {
            item.PlanQty = item.Qty;
            if (!bundles.TryGetValue(item.Id, out var bundle))
            {
                item.Ops = new List<WorkOrderListOpDto>();
                item.DoneQty = 0;
                item.RemainQty = Math.Max(0, item.Qty);
                item.ProgressPercent = null;
                item.ProgressHint = WorkOrderProgressCalculator.HintNoOps;
                continue;
            }

            item.Ops = bundle.Ops.Select(o => new WorkOrderListOpDto
            {
                OperationName = o.OperationName,
                PlanQty = o.PlanQty,
                DoneQty = o.DoneQty
            }).ToList();
            item.DoneQty = bundle.Progress.DoneQty;
            item.RemainQty = bundle.Progress.RemainQty;
            item.ProgressPercent = bundle.Progress.ProgressPercent;
            item.ProgressHint = bundle.Progress.ProgressHint;
        }
    }

    /// <summary>
    /// docs/422：执行中且未重开、进度已报满 → 静默纠成已结束（脏数据/漏重算）。返回实际纠偏条数。
    /// </summary>
    private async Task<int> HealStaleDoingAsync(List<WorkOrderListDto> list, long factoryId)
    {
        var staleIds = list
            .Where(x => x.Status == 1 && !x.Reopened && IsProgressFull(x))
            .Select(x => x.Id)
            .ToList();
        if (staleIds.Count == 0) return 0;

        var rows = await _db.WorkOrders
            .Where(o => o.FactoryId == factoryId && staleIds.Contains(o.Id) && o.Status == 1 && !o.Reopened)
            .ToListAsync();
        if (rows.Count == 0) return 0;

        foreach (var o in rows)
            o.Status = 2;
        await _db.SaveChangesAsync();

        var healed = rows.Select(r => r.Id).ToHashSet();
        foreach (var item in list)
        {
            if (healed.Contains(item.Id))
            {
                item.Status = 2;
                item.Reopened = false;
            }
        }
        return rows.Count;
    }

    private static bool IsProgressFull(WorkOrderListDto item)
    {
        if (item.Ops.Count > 0)
            return item.Ops.All(o => o.PlanQty <= 0 || o.DoneQty >= o.PlanQty);
        return item.DoneQty >= item.Qty;
    }

    public async Task<ApiResult<WorkOrderStatusCountsDto>> GetStatusCountsAsync(long factoryId)
    {
        var rows = await _db.WorkOrders.AsNoTracking()
            .Where(o => o.FactoryId == factoryId)
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Cnt = g.Count() })
            .ToListAsync();

        var map = rows.ToDictionary(x => x.Status, x => x.Cnt);
        var notStarted = map.GetValueOrDefault((byte)0, 0);
        var doing = map.GetValueOrDefault((byte)1, 0);
        var done = map.GetValueOrDefault((byte)2, 0);
        var cancelled = map.GetValueOrDefault((byte)3, 0);
        var draft = map.GetValueOrDefault((byte)4, 0);
        var pendingAudit = map.GetValueOrDefault((byte)5, 0);
        var suspended = map.GetValueOrDefault((byte)6, 0);
        return ApiResult<WorkOrderStatusCountsDto>.Ok(new WorkOrderStatusCountsDto
        {
            NotStarted = notStarted,
            Doing = doing,
            Done = done,
            Cancelled = cancelled,
            Draft = draft,
            PendingAudit = pendingAudit,
            Suspended = suspended,
            // 「全部」与 H5 一致：不含已作废、已挂起、草稿、待审核
            All = notStarted + doing + done
        });
    }

    public async Task<ApiResult<WorkOrderDetailDto>> GetAsync(long id, long factoryId)
    {
        var order = await _db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(GetAsync), "工单不存在");
        // PC 按 Id 取详情：不按报工权限过滤 tasks（管理侧要看全工序）
        return ApiResult<WorkOrderDetailDto>.Ok(await BuildDetailAsync(order));
    }

    public async Task<ApiResult<WorkOrderDetailDto>> GetByOrderNoAsync(
        string orderNo, long factoryId, long? reportPermissionUserId, byte? reportPermissionRole)
    {
        if (string.IsNullOrWhiteSpace(orderNo))
            throw ThrowHelper.Biz(nameof(GetByOrderNoAsync), "扫描到的工单号为空，请重新对准二维码扫描；如多次失败，可手动输入工单号查询。");

        var order = await _db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrderNo == orderNo.Trim() && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(GetByOrderNoAsync), $"未找到工单号 {orderNo} 的工单，请确认二维码是否对应本厂工单，或手动输入工单号到「工单」中搜索核实。");
        // H5/扫码：传入当前用户以按报工权限过滤 tasks；AI/其它查进度不传则看全工序
        return ApiResult<WorkOrderDetailDto>.Ok(
            await BuildDetailAsync(order, reportPermissionUserId, reportPermissionRole));
    }

    private async Task<WorkOrderDetailDto> BuildDetailAsync(ProdWorkOrder order, long? filterUserId = null, byte? filterRole = null)
    {
        await TenantDataGuard.WorkOrdersAsync(_db, order.FactoryId, new[] { order.Id });
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == order.ProductId && p.FactoryId == order.FactoryId)
            ?? throw ThrowHelper.Biz(nameof(BuildDetailAsync), "该工单关联的产品已被删除，无法查看详情。请到「工单」检查该工单，必要时重新关联产品或重建工单。");

        var fieldIds = _db.CustomFields.Where(f => f.FactoryId == order.FactoryId && f.Target == "work_order").Select(f => f.Id);
        var ext = await _db.CustomFieldValues.AsNoTracking()
            .Where(v => v.TargetId == order.Id && fieldIds.Contains(v.FieldId))
            .ToDictionaryAsync(v => v.FieldId, v => v.Value ?? "");

        var tasks = await LoadTasksAsync(order);
        if (filterUserId.HasValue && filterRole.HasValue)
        {
            var allowed = await ReportPermissionHelper.FilterAllowedOperationIdsAsync(
                _db, filterUserId.Value, filterRole.Value, tasks.Select(t => t.OperationId));
            tasks = tasks.Where(t => allowed.Contains(t.OperationId)).ToList();
        }

        var status = order.Status;
        var reopened = order.Reopened;
        if (status == 1 && !reopened && IsTasksFull(tasks, order.Qty))
        {
            var tracked = await _db.WorkOrders.FirstOrDefaultAsync(o => o.Id == order.Id && o.FactoryId == order.FactoryId);
            if (tracked != null && tracked.Status == 1 && !tracked.Reopened)
            {
                tracked.Status = 2;
                await _db.SaveChangesAsync();
                status = 2;
                reopened = false;
            }
        }

        var userIds = new List<long>();
        if (order.CreatedBy is long cb) userIds.Add(cb);
        if (order.AuditedBy is long ab) userIds.Add(ab);
        var nameMap = userIds.Count == 0
            ? new Dictionary<long, string>()
            : await _db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Name);

        return new WorkOrderDetailDto
        {
            Id = order.Id,
            OrderNo = order.OrderNo,
            ProductId = product.Id,
            ProductCode = product.Code,
            ProductName = product.Name,
            Qty = order.Qty,
            Status = status,
            Reopened = reopened,
            DueDate = order.DueDate,
            DueState = DueStateHelper.Calc(status, order.DueDate, DateTime.Now),
            CreatedByName = order.CreatedBy is long c && nameMap.TryGetValue(c, out var cn) ? cn : "",
            CreatedAt = order.CreatedAt,
            AuditedByName = order.AuditedBy is long a && nameMap.TryGetValue(a, out var an) ? an : "",
            AuditedAt = order.AuditedAt,
            Ext = ext,
            Tasks = tasks
        };
    }

    private static bool IsTasksFull(List<WorkOrderTaskDto> tasks, int orderQty)
    {
        if (tasks.Count > 0)
            return tasks.All(t => t.PlanQty <= 0 || t.DoneQty >= t.PlanQty);
        return false; // 详情无任务行时不在此纠偏（避免与列表无 ops 兜底口径纠缠）
    }

    private async Task<List<WorkOrderTaskDto>> LoadTasksAsync(ProdWorkOrder order)
    {
        var reportedOps = (await _db.Reports.Where(r => r.OrderId == order.Id).Select(r => r.OperationId).Distinct().ToListAsync()).ToHashSet();
        var reportSum = await (from r in _db.Reports.AsNoTracking()
                               where r.OrderId == order.Id && r.ReviewStatus != 2
                               group r by r.OperationId into g
                               select new
                               {
                                   OperationId = g.Key,
                                   Done = g.Sum(x => x.GoodQty),
                                   Defect = g.Sum(x => x.DefectQty)
                               }).ToListAsync();
        var goodMap = reportSum.ToDictionary(x => x.OperationId, x => x.Done);
        var defectMap = reportSum.ToDictionary(x => x.OperationId, x => x.Defect);

        var tasks = await _db.WorkOrderOperations.AsNoTracking()
            .Where(t => t.WorkOrderId == order.Id)
            .OrderBy(t => t.Seq)
            .ToListAsync();

        // 旧数据兜底：无任务行时按产品当前路线生成（不落库）
        if (tasks.Count == 0)
        {
            var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == order.ProductId);
            var ops = new List<WorkOrderTaskDto>();
            if (product?.RoutingId != null)
            {
                ops = await (from s in _db.RoutingSteps.AsNoTracking()
                             where s.RoutingId == product.RoutingId
                             join op in _db.Operations.AsNoTracking().Where(op => op.FactoryId == order.FactoryId) on s.OperationId equals op.Id
                             orderby s.Seq
                             select new WorkOrderTaskDto
                             {
                                 OperationId = op.Id,
                                 OperationName = op.Name,
                                 Seq = s.Seq,
                                 PlanQty = order.Qty
                             }).ToListAsync();
            }
            foreach (var t in ops)
            {
                t.DoneQty = goodMap.GetValueOrDefault(t.OperationId, 0);
                t.DefectQty = defectMap.GetValueOrDefault(t.OperationId, 0);
            }
            return ops;
        }

        var names = await _db.Operations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name);
        var taskIds = tasks.Select(t => t.Id).ToList();
        var assigneeRows = taskIds.Count == 0
            ? new List<(long TaskId, long UserId)>()
            : (await _db.WorkOrderOperationAssignees.AsNoTracking()
                .Where(a => taskIds.Contains(a.WorkOrderOperationId))
                .Select(a => new { a.WorkOrderOperationId, a.UserId })
                .ToListAsync())
                .Select(a => (a.WorkOrderOperationId, a.UserId))
                .ToList();
        // 兼容仅有冗余列的旧数据
        foreach (var t in tasks.Where(t => t.AssigneeUserId != null))
        {
            if (!assigneeRows.Any(a => a.Item1 == t.Id && a.Item2 == t.AssigneeUserId))
                assigneeRows.Add((t.Id, t.AssigneeUserId!.Value));
        }
        var allAssigneeIds = assigneeRows.Select(a => a.Item2).Distinct().ToList();
        var assigneeNames = allAssigneeIds.Count > 0
            ? await _db.Users.AsNoTracking().Where(u => allAssigneeIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name)
            : new Dictionary<long, string>();
        var deptIds = tasks.Where(t => t.AssigneeDepartmentId != null).Select(t => t.AssigneeDepartmentId!.Value).Distinct().ToList();
        var deptNames = deptIds.Count > 0
            ? await _db.Departments.AsNoTracking().Where(d => deptIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name)
            : new Dictionary<long, string>();
        var taskAttributes = new Dictionary<long, Dictionary<long, string?>>();
        foreach (var task in tasks) taskAttributes[task.Id] = await new OperationAttributeService(_db).GetAsync(task.Id, order.FactoryId, task.OperationId);
        return tasks.Select(t =>
        {
            var list = assigneeRows.Where(a => a.Item1 == t.Id)
                .Select(a => new WorkOrderAssigneeDto
                {
                    UserId = a.Item2,
                    UserName = assigneeNames.GetValueOrDefault(a.Item2, "")
                }).ToList();
            var first = list.FirstOrDefault();
            var deptName = t.AssigneeDepartmentId == null
                ? ""
                : deptNames.GetValueOrDefault(t.AssigneeDepartmentId.Value, "");
            var personName = first?.UserName
                ?? (t.AssigneeUserId == null ? "" : assigneeNames.GetValueOrDefault(t.AssigneeUserId.Value, ""));
            return new WorkOrderTaskDto
            {
                Id = t.Id,
                OperationAttributes = taskAttributes[t.Id],
                HasReports = reportedOps.Contains(t.OperationId),
                ProcessingPrice = t.ProcessingPrice == null ? null : OperationPricingService.Read<ProcessingQuote>(t.ProcessingPrice),
                OperationId = t.OperationId,
                OperationName = names.GetValueOrDefault(t.OperationId, ""),
                Seq = t.Seq,
                PlanQty = t.PlanQty,
                DoneQty = goodMap.GetValueOrDefault(t.OperationId, 0),
                DefectQty = defectMap.GetValueOrDefault(t.OperationId, 0),
                AssigneeUserId = first?.UserId ?? t.AssigneeUserId,
                AssigneeName = !string.IsNullOrEmpty(deptName) ? $"班组·{deptName}" : personName,
                Assignees = list,
                AssigneeDepartmentId = t.AssigneeDepartmentId,
                AssigneeDepartmentName = deptName
            };
        }).ToList();
    }

    public async Task<ApiResult<object?>> CreateAsync(WorkOrderCreateDto dto, long currentUserId, long factoryId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await OperationPriceListService.LockFactoryAsync(_db,factoryId);
        if (dto.CopyFromId.HasValue && !await _db.WorkOrders.AnyAsync(o => o.Id == dto.CopyFromId && o.FactoryId == factoryId))
            throw ThrowHelper.Biz(nameof(CreateAsync), "找不到要复制的工单，可能工单号有误或该工单不属于本厂。请到「工单」中搜索确认源工单是否存在且归属正确。");
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == dto.ProductId && p.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(CreateAsync), "产品不存在");
        if (product.Status == 0)
            throw ThrowHelper.Biz(nameof(CreateAsync), "该产品已禁用，不能用来新建工单。请到「产品定义」反禁用，或换一个启用中的产品。");
        if (product.AuditStatus != 1)
            throw ThrowHelper.Biz(nameof(CreateAsync), "该产品还没审核，不能用来新建工单。请到「产品定义」点「审核」后再选。");
        if (dto.Qty <= 0)
            throw ThrowHelper.Biz(nameof(CreateAsync), "生产数量必须填写大于0的数字。");
        var extIds = (dto.Ext ?? new()).Keys.ToList();
        if (await _db.CustomFields.CountAsync(f => f.FactoryId == factoryId && f.Target == "work_order" && extIds.Contains(f.Id)) != extIds.Count)
            throw ThrowHelper.Biz(nameof(CreateAsync), "工单中包含无效的自定义字段，可能字段已被删除、不属于本厂或不是工单类型字段。请到「自定义字段管理」核实后重新填写。");
        if (product.RoutingId.HasValue)
        {
            if (!await _db.Routings.AnyAsync(r => r.Id == product.RoutingId && r.FactoryId == factoryId))
                throw ThrowHelper.Biz(nameof(CreateAsync), "该产品没有找到有效工艺路线，或工艺路线不属于本厂。请到「工艺路线管理」中为该产品设置正确的工艺路线后再建工单。");
            var steps = await _db.RoutingSteps.Where(s => s.RoutingId == product.RoutingId).Select(s => s.OperationId).ToListAsync();
            if (steps.Distinct().Count() != steps.Count || await _db.Operations.CountAsync(o => o.FactoryId == factoryId && steps.Contains(o.Id)) != steps.Count)
                throw ThrowHelper.Biz(nameof(CreateAsync), "该产品的工艺路线中存在重复、已失效或非本厂工序，无法用于建工单。请到「工艺路线管理」打开对应路线，修正问题工序后再试。");
        }

        var orderNo = await NextOrderNoAsync(dto.OrderNo, factoryId);

        var action = ResolveSaveAction(dto.AsDraft, dto.SaveAction);
        var order = new ProdWorkOrder
        {
            FactoryId = factoryId,
            OrderNo = orderNo,
            ProductId = product.Id,
            Qty = dto.Qty,
            Status = action switch
            {
                "draft" => (byte)4,
                "pending" => (byte)5,
                _ => (byte)0
            },
            DueDate = dto.DueDate,
            CreatedBy = currentUserId,
            CreatedAt = DateTime.Now
        };
        _db.WorkOrders.Add(order);
        await _db.SaveChangesAsync();

        await SyncTasksAsync(order, product.RoutingId, dto.Qty, null);
        if (dto.CopyFromId.HasValue && dto.OperationAttributes == null)
        {
            var source = await _db.WorkOrders.AsNoTracking().SingleAsync(o => o.Id == dto.CopyFromId && o.FactoryId == factoryId);
            if (source.ProductId == dto.ProductId) dto.OperationAttributes = await new OperationAttributeService(_db).ReadOrderAsync(source.Id, factoryId);
        }
        await new OperationAttributeService(_db).SaveOrderAsync(order, dto.OperationAttributes);
        // docs/435：仅审核锁价；暂存/待审不 BindAsync
        if (action == "approve")
        {
            await new OperationPricingService(_db).BindAsync(order, dto.QuoteTokens);
            StampFirstAudit(order, currentUserId);
        }
        else
            order.Status = action == "draft" ? (byte)4 : (byte)5;
        await SaveExtAsync(order.Id, dto.Ext);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> UpdateAsync(long id, WorkOrderUpdateDto dto, long currentUserId, long factoryId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await OperationPriceListService.LockFactoryAsync(_db,factoryId);
        var order = await _db.WorkOrders.FirstOrDefaultAsync(o => o.Id == id && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "工单不存在");

        await WechatEventService.LockOrderAsync(_db, order);
        if (order.Status == 3)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "该工单已作废，不能再编辑。如需调整，请先在「工单」中恢复该工单，或新建一张工单。");
        if (order.Status == 6)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "该工单已挂起，不能再编辑。如需调整，请先在「工单」中点「恢复生产」后再改。");
        if (order.Status == 2)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "这张工单已经结束了，不能修改。如果需要改，请先在电脑端把工单撤回再修改。");
        var action = ResolveSaveAction(dto.AsDraft, dto.SaveAction);
        // docs/435：已生效(0/1)不能退回草稿/待审；仅草稿/待审可用 draft|pending
        if (!IsPreAuditStatus(order.Status) && action is "draft" or "pending")
            throw ThrowHelper.Biz(nameof(UpdateAsync), "工单已经审核并锁价，不能改回草稿或已保存。如需改工艺单价，请复制新单重新审核；本单请直接保存数量、交期等修改。");

        // 工单号
        if (!string.IsNullOrWhiteSpace(dto.OrderNo))
        {
            var no = dto.OrderNo.Trim();
            if (await _db.WorkOrders.AnyAsync(o => o.FactoryId == factoryId && o.OrderNo == no && o.Id != id))
                throw ThrowHelper.Biz(nameof(UpdateAsync), "该工单号已被使用。请到「工单」中搜索确认；可使用系统自动生成的工单号，或换一个未占用的编号。");
            order.OrderNo = no;
        }

        // 计划交期（改交期无报工限制，可直接改；null 表示清空）
        order.DueDate = dto.DueDate;

        // 换产品
        if (dto.ProductId.HasValue && dto.ProductId.Value != order.ProductId)
        {
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == dto.ProductId.Value && p.FactoryId == factoryId)
                ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "产品不存在");
            if (product.Status == 0)
                throw ThrowHelper.Biz(nameof(UpdateAsync), "该产品已禁用，不能换到这个产品。请到「产品定义」反禁用，或换一个启用中的产品。");
            if (product.AuditStatus != 1)
                throw ThrowHelper.Biz(nameof(UpdateAsync), "该产品还没审核，不能换到这个产品。请到「产品定义」点「审核」后再选。");
            var hasReport = await _db.Reports.AnyAsync(r => r.OrderId == id);
            if (hasReport)
                throw ThrowHelper.Biz(nameof(UpdateAsync), "这张工单已经有人报过工了，不能更换产品。如需更换产品，请新建一张工单。");
            order.ProductId = product.Id;
            order.Qty = dto.Qty ?? order.Qty;
            await SyncTasksAsync(order, product.RoutingId, order.Qty, null);
        }
        else if (dto.Qty.HasValue)
        {
            // 改数量
            if (dto.Qty.Value <= 0)
                throw ThrowHelper.Biz(nameof(UpdateAsync), "数量必须大于0");
            var hasReport = await _db.Reports.AnyAsync(r => r.OrderId == id);
            var totalGood = await _db.Reports
                .Where(r => r.OrderId == id && r.ReviewStatus != 2)
                .SumAsync(r => (int?)r.GoodQty) ?? 0;
            if (hasReport && dto.Qty.Value < totalGood)
                throw ThrowHelper.Biz(nameof(UpdateAsync), $"工单总量不能少于已报合格品数 {totalGood}。如需减少总量，请先在「报工」中调整对应报工数据。");

            order.Qty = dto.Qty.Value;
            // 无报工时同步各任务计划数；有报工仅改工单级 qty
            if (!hasReport)
                await SyncTasksAsync(order, null, dto.Qty.Value, null, preserveExisting: true);
        }

        // 工序计划数（有任务时按行改）
        if (dto.PlanQty != null && dto.PlanQty.Count > 0)
        {
            var existing = await _db.WorkOrderOperations.Where(t => t.WorkOrderId == id).ToListAsync();
            var reportSum = await (from r in _db.Reports
                                   where r.OrderId == id && r.ReviewStatus != 2
                                   group r by r.OperationId into g
                                   select new { OperationId = g.Key, Done = g.Sum(x => x.GoodQty) })
                                   .ToDictionaryAsync(x => x.OperationId, x => x.Done);
            foreach (var plan in dto.PlanQty)
            {
                var task = existing.FirstOrDefault(t => t.OperationId == plan.OperationId);
                if (task == null) continue;
                var done = reportSum.GetValueOrDefault(plan.OperationId, 0);
                if (plan.PlanQty < done)
                    throw ThrowHelper.Biz(nameof(UpdateAsync), $"该工序计划数量不能少于已报合格品数 {done}。请检查该工序报工记录，如需调低计划数先修正报工数据。");
                task.PlanQty = plan.PlanQty;
            }
        }

        await _db.SaveChangesAsync();

        if (dto.Ext != null)
            await SaveExtAsync(id, dto.Ext);

        await new OperationAttributeService(_db).SaveOrderAsync(order, dto.OperationAttributes);
        if (action == "draft")
        {
            order.Status = 4;
        }
        else if (action == "pending")
        {
            order.Status = 5;
        }
        else
        {
            // 审核或已生效单保存：必须锁价成功
            var firstAudit = IsPreAuditStatus(order.Status);
            await new OperationPricingService(_db).BindAsync(order, dto.QuoteTokens);
            if (firstAudit)
            {
                order.Status = 0;
                StampFirstAudit(order, currentUserId);
            }
        }
        if (!IsPreAuditStatus(order.Status))
            await RecalcStatusAsync(order);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> TransitionAsync(long id, string action, long factoryId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        await OperationPriceListService.LockFactoryAsync(_db,factoryId);
        var order = await _db.WorkOrders.FirstOrDefaultAsync(o => o.Id == id && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(TransitionAsync), "工单不存在");

        await WechatEventService.LockOrderAsync(_db, order);
        var beforeStatus = order.Status;
        switch (action)
        {
            case "start":
                if (IsPreAuditStatus(order.Status)) throw ThrowHelper.Biz(nameof(TransitionAsync), "这张工单还没审核锁价，请先在电脑端点「审核」后再点「开始」。");
                if (order.Status != 0) throw ThrowHelper.Biz(nameof(TransitionAsync), "该工单不在「已审核」状态，无法开始。请确认工单当前状态是否正确，或刷新页面后重试。");
                order.Status = 1;
                order.Reopened = false;
                break;
            case "finish":
                if (order.Status != 1) throw ThrowHelper.Biz(nameof(TransitionAsync), "该工单不在「报工中」状态，无法结束。请确认工单是否已开始生产，或刷新页面查看最新状态。");
                order.Status = 2;
                order.Reopened = false;
                break;
            case "withdraw":
                // docs/422：已结束→执行中标记重开，报满不再自动结束
                if (order.Status == 2)
                {
                    order.Status = 1;
                    order.Reopened = true;
                }
                else if (order.Status == 1)
                {
                    order.Status = 0;
                    order.Reopened = false;
                }
                else throw ThrowHelper.Biz(nameof(TransitionAsync), "该工单当前状态不允许撤回，请到工单详情页确认当前流转节点。");
                break;
            case "cancel":
                // 草稿(4)/待审核(5)/已挂起(6)也可作废
                if (order.Status == 2 || order.Status == 3) throw ThrowHelper.Biz(nameof(TransitionAsync), "该工单当前状态不允许作废。通常只有已审核、报工中、已挂起、草稿或已保存的工单可以作废，请确认工单状态后重试。");
                order.Status = 3;
                order.Reopened = false;
                break;
            case "restore":
                if (order.Status != 3) throw ThrowHelper.Biz(nameof(TransitionAsync), "该工单未被作废，不需要恢复操作；要恢复已作废工单，请在「工单」中找到已作废状态的工单再点恢复。已挂起请用「恢复生产」。");
                order.Status = 0;
                order.Reopened = false;
                break;
            case "hang":
                // docs/437：报工中 → 已挂起（暂停还要继续）
                if (order.Status != 1) throw ThrowHelper.Biz(nameof(TransitionAsync), "只有报工中的工单可以挂起。请确认工单当前状态后重试。");
                order.Status = 6;
                order.Reopened = false;
                break;
            case "unhang":
                // docs/437：已挂起 → 报工中
                if (order.Status != 6) throw ThrowHelper.Biz(nameof(TransitionAsync), "该工单未挂起，不需要恢复生产；已作废请用「恢复」。");
                order.Status = 1;
                order.Reopened = false;
                break;
            case "unaudit":
                // 反审核：仅已锁价且无报工；解锁单价，退回已保存(5)
                if (IsPreAuditStatus(order.Status) || order.Status is 2 or 3)
                    throw ThrowHelper.BizUser("当前不需要反审核");
                if (order.Status is not (0 or 1))
                    throw ThrowHelper.BizUser("当前不需要反审核");
                if (await _db.Reports.AnyAsync(r => r.OrderId == order.Id))
                    throw ThrowHelper.BizUser("已报工，操作终止");
                var lockedTasks = await _db.WorkOrderOperations.Where(t => t.WorkOrderId == order.Id).ToListAsync();
                foreach (var t in lockedTasks) t.ProcessingPrice = null;
                order.AuditedBy = null;
                order.AuditedAt = null;
                order.Status = 5;
                order.Reopened = false;
                break;
            default:
                throw ThrowHelper.Biz(nameof(TransitionAsync), $"系统无法识别该操作指令，可能是页面版本不一致导致。请刷新页面后重试；若仍出现请联系技术支持并提供追踪号。");
        }

        await _db.SaveChangesAsync();
        if (_events != null) await _events.EnqueueAsync(order, beforeStatus);
        await tx.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> CopyAsync(long id, long currentUserId, long factoryId)
    {
        var src = await _db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(CopyAsync), "工单不存在");

        var fields = _db.CustomFields.Where(f => f.FactoryId == factoryId && f.Target == "work_order").Select(f => f.Id);
        var ext = await _db.CustomFieldValues.AsNoTracking()
            .Where(v => v.TargetId == id && fields.Contains(v.FieldId))
            .ToDictionaryAsync(v => v.FieldId, v => v.Value ?? "");
        return await CreateAsync(new WorkOrderCreateDto
        {
            CopyFromId = id, ProductId = src.ProductId, Qty = src.Qty, Ext = ext
        }, currentUserId, factoryId);
    }

    public async Task<ApiResult<object?>> DeleteAsync(long id)
    {
        var order = await _db.WorkOrders.FindAsync(id)
            ?? throw ThrowHelper.Biz(nameof(DeleteAsync), "工单不存在");

        _db.WorkSessions.RemoveRange(_db.WorkSessions.Where(x => x.WorkOrderId == id));
        _db.Reports.RemoveRange(_db.Reports.Where(r => r.OrderId == id));
        await new OperationAttributeService(_db).RemoveTasksAsync(id, order.FactoryId);
        _db.WorkOrderOperations.RemoveRange(_db.WorkOrderOperations.Where(t => t.WorkOrderId == id));
        _db.CustomFieldValues.RemoveRange(_db.CustomFieldValues.Where(v => v.TargetId == id && _db.CustomFields.Any(f => f.Id == v.FieldId && f.FactoryId == order.FactoryId && f.Target == "work_order")));
        _db.WorkOrders.Remove(order);
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<string>> GetQrContentAsync(long id, long factoryId)
    {
        var order = await _db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(GetQrContentAsync), "工单不存在");
        return ApiResult<string>.Ok(order.OrderNo);
    }

    // ---- helpers ----

    private async Task<string> NextOrderNoAsync(string? custom, long factoryId)
    {
        if (!string.IsNullOrWhiteSpace(custom))
        {
            var no = custom.Trim();
            if (await _db.WorkOrders.AnyAsync(o => o.FactoryId == factoryId && o.OrderNo == no))
                throw ThrowHelper.Biz(nameof(NextOrderNoAsync), "工单号不可重复");
            return no;
        }
        // 用库内最大 A0001–Z9999 尾号 +1，忽略历史 GD 长单号；避免删单后 Count 回退撞唯一索引
        var existing = await _db.WorkOrders.AsNoTracking()
            .Where(o => o.FactoryId == factoryId)
            .Select(o => o.OrderNo)
            .ToListAsync();
        var letter = 'A';
        var maxSeq = 0;
        var any = false;
        foreach (var no in existing)
        {
            if (no.Length != 5) continue;
            var ch = no[0];
            if (ch is < 'A' or > 'Z') continue;
            if (!int.TryParse(no.AsSpan(1, 4), out var seq)) continue;
            if (!any || ch > letter || (ch == letter && seq > maxSeq))
            {
                letter = ch;
                maxSeq = seq;
                any = true;
            }
        }
        if (!any)
            return "A0001";
        if (maxSeq < 9999)
            return letter + (maxSeq + 1).ToString("D4");
        if (letter >= 'Z')
            throw ThrowHelper.Biz(nameof(NextOrderNoAsync), "系统自动生成的工单号已全部用完。请在「系统配置」中扩展单号规则，或改用自定义工单号继续建单。");
        return ((char)(letter + 1)) + "0001";
    }

    /// <summary>
    /// 重建工单工序任务。routingId 为 null 时沿用现有（用于只改计划数的场景）。
    /// </summary>
    private async Task SyncTasksAsync(ProdWorkOrder order, long? routingId, int planQty, List<WorkOrderPlanDto>? plans, bool preserveExisting = false)
    {
        if (!routingId.HasValue && preserveExisting)
        {
            var existing = await _db.WorkOrderOperations.Where(t => t.WorkOrderId == order.Id).ToListAsync();
            foreach (var t in existing) t.PlanQty = plans?.FirstOrDefault(p => p.OperationId == t.OperationId)?.PlanQty ?? planQty;
            await _db.SaveChangesAsync();
            return;
        }
        await new OperationAttributeService(_db).RemoveTasksAsync(order.Id, order.FactoryId);
        _db.WorkOrderOperations.RemoveRange(_db.WorkOrderOperations.Where(t => t.WorkOrderId == order.Id));
        await _db.SaveChangesAsync();

        var steps = new List<(long OperationId, int Seq)>();
        if (routingId.HasValue)
        {
            steps = await _db.RoutingSteps.AsNoTracking()
                .Where(s => s.RoutingId == routingId)
                .OrderBy(s => s.Seq)
                .Select(s => new ValueTuple<long, int>(s.OperationId, s.Seq)).ToListAsync();
        }

        foreach (var s in steps)
        {
            var p = plans?.FirstOrDefault(x => x.OperationId == s.OperationId);
            _db.WorkOrderOperations.Add(new ProdWorkOrderOperation
            {
                WorkOrderId = order.Id,
                OperationId = s.OperationId,
                Seq = s.Seq,
                PlanQty = p?.PlanQty ?? planQty
            });
        }
        await _db.SaveChangesAsync();
    }

    private async Task SaveExtAsync(long orderId, Dictionary<long, string>? ext)
    {
        if (ext == null) return;
        foreach (var kv in ext)
        {
            var existing = await _db.CustomFieldValues.FirstOrDefaultAsync(v => v.TargetId == orderId && v.FieldId == kv.Key);
            if (existing != null) existing.Value = kv.Value;
            else _db.CustomFieldValues.Add(new SysCustomFieldValue { FieldId = kv.Key, TargetId = orderId, Value = kv.Value });
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>按工序任务重算工单完成状态。注意：本方法不修改已作废(3)、已挂起(6)。docs/422：reopened 时报满保持执行中。</summary>
    public async Task RecalcStatusAsync(ProdWorkOrder order)
    {
        await using var ownedTx = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync() : null;
        await OperationPriceListService.LockFactoryAsync(_db,order.FactoryId);
        await WechatEventService.LockOrderAsync(_db, order);
        var beforeStatus = order.Status;
        if (order.Status == 3 || order.Status == 6) return;
        // docs/425/435：草稿/待审核不参与报工驱动的状态重算
        if (IsPreAuditStatus(order.Status)) return;

        var hasReport = await _db.Reports.AnyAsync(r => r.OrderId == order.Id && r.ReviewStatus != 2);
        if (!hasReport)
        {
            if (order.Status == 1) order.Status = 0; // 无报工回到未开始
            order.Reopened = false;
            await _db.SaveChangesAsync();
            if (_events != null) await _events.EnqueueAsync(order, beforeStatus);
            if (ownedTx != null) await ownedTx.CommitAsync();
            return;
        }

        order.Status = 1;

        var tasks = await _db.WorkOrderOperations.Where(t => t.WorkOrderId == order.Id).ToListAsync();
        bool allDone;
        if (tasks.Count == 0)
        {
            // 旧数据：无任务行，按工单级 qty 判完成
            var totalGood = await _db.Reports
                .Where(r => r.OrderId == order.Id && r.ReviewStatus != 2)
                .SumAsync(r => (int?)r.GoodQty) ?? 0;
            allDone = totalGood >= order.Qty;
        }
        else
        {
            var reportSum = await (from r in _db.Reports
                                   where r.OrderId == order.Id && r.ReviewStatus != 2
                                   group r by r.OperationId into g
                                   select new { OperationId = g.Key, Done = g.Sum(x => x.GoodQty) })
                                   .ToDictionaryAsync(x => x.OperationId, x => x.Done);
            allDone = tasks.All(t => reportSum.GetValueOrDefault(t.OperationId, 0) >= t.PlanQty);
        }

        if (allDone)
        {
            if (!order.Reopened) order.Status = 2;
        }
        else
        {
            order.Reopened = false; // 进度回落：恢复自动结束
        }

        await _db.SaveChangesAsync();
        if (_events != null) await _events.EnqueueAsync(order, beforeStatus);
        if (ownedTx != null) await ownedTx.CommitAsync();
    }
}
