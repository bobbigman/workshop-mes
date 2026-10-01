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
    Task<ApiResult<object?>> UpdateAsync(long id, WorkOrderUpdateDto dto, long factoryId);
    Task<ApiResult<object?>> TransitionAsync(long id, string action, long factoryId);
    Task<ApiResult<object?>> CopyAsync(long id, long factoryId);
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
    public bool ExcludeCancelled { get; set; }  // H5 列表排除已取消(3)
    /// <summary>为 true 时只返回未结束工单（status 非 2 非 3），供报工下拉。</summary>
    public bool ExcludeFinished { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    /// <summary>可选；须为本厂 work_order 字段。值空白时不筛，但仍校验 ID。</summary>
    public long? CustomFieldId { get; set; }
    public string? CustomFieldValue { get; set; }
    /// <summary>exact | contains；默认 exact。单选/数字仅 exact。</summary>
    public string? CustomFieldMatch { get; set; }
}

public class WorkOrderStatusCountsDto
{
    public int All { get; set; }
    public int NotStarted { get; set; }
    public int Doing { get; set; }
    public int Done { get; set; }
    public int Cancelled { get; set; }
}

public class WorkOrderCreateDto
{
    public string? OrderNo { get; set; }             // 可自定义，空则自动生成
    public long ProductId { get; set; }
    public int Qty { get; set; }
    public DateTime? DueDate { get; set; }           // 计划交期，可空
    public Dictionary<long, string> Ext { get; set; } = new();
}

public class WorkOrderPlanDto
{
    public long OperationId { get; set; }
    public int PlanQty { get; set; }
}

public class WorkOrderUpdateDto
{
    public string? OrderNo { get; set; }             // 可改
    public long? ProductId { get; set; }             // 可换
    public int? Qty { get; set; }                    // 可改
    public DateTime? DueDate { get; set; }           // 计划交期，可空
    public List<WorkOrderPlanDto>? PlanQty { get; set; } // 工序计划数
    public Dictionary<long, string>? Ext { get; set; }   // 自定义字段
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
    public DateTime? DueDate { get; set; }
    public string DueState { get; set; } = "";       // normal/warning/overdue
    public DateTime CreatedAt { get; set; }
    public List<WorkOrderListOpDto> Ops { get; set; } = new();
    /// <summary>仅含本厂已开启「在工单列表显示」的工单字段；键为字段 ID。</summary>
    public Dictionary<long, string> Ext { get; set; } = new();
}

public class WorkOrderTaskDto
{
    public long Id { get; set; }               // prod_work_order_operation.id；旧数据兜底临时任务为 0
    public long OperationId { get; set; }
    public string OperationName { get; set; } = "";
    public int Seq { get; set; }
    public int PlanQty { get; set; }
    public int DoneQty { get; set; }       // 该工序已报良品合计
    public int DefectQty { get; set; }     // 该工序已报不良合计
    public long? AssigneeUserId { get; set; }   // 派工执行人（docs/29）；空=未派工
    public string AssigneeName { get; set; } = "";
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
    public DateTime? DueDate { get; set; }
    public string DueState { get; set; } = "";       // normal/warning/overdue
    public Dictionary<long, string> Ext { get; set; } = new();
    public List<WorkOrderTaskDto> Tasks { get; set; } = new();
}

public class WorkOrderService : IWorkOrderService
{
    private readonly AppDbContext _db;
    public WorkOrderService(AppDbContext db) { _db = db; }

    public async Task<ApiResult<PageResult<WorkOrderListDto>>> QueryAsync(WorkOrderQueryDto query, long factoryId)
    {
        var q = from o in _db.WorkOrders.AsNoTracking()
                where o.FactoryId == factoryId
                join p in _db.Products.AsNoTracking() on o.ProductId equals p.Id
                select new { o, ProductName = p.Name, ProductCode = p.Code };

        if (!string.IsNullOrWhiteSpace(query.Keyword))
            q = q.Where(x => x.o.OrderNo.Contains(query.Keyword) || x.ProductName.Contains(query.Keyword) || x.ProductCode.Contains(query.Keyword));
        if (query.Status.HasValue)
            q = q.Where(x => x.o.Status == query.Status.Value);
        else if (query.ExcludeFinished)
            q = q.Where(x => x.o.Status != 2 && x.o.Status != 3);
        else if (query.ExcludeCancelled)
            q = q.Where(x => x.o.Status != 3);

        var filter = await ParseCustomFieldFilterAsync(query, factoryId);
        if (filter != null)
        {
            var fieldId = filter.Value.FieldId;
            var value = filter.Value.Value;
            if (filter.Value.Match == "exact")
            {
                q = q.Where(x => _db.CustomFieldValues.AsNoTracking()
                    .Any(v => v.FieldId == fieldId && v.Value == value && v.TargetId == x.o.Id));
            }
            else
            {
                var likePattern = "%" + EscapeLikePattern(value) + "%";
                q = q.Where(x => _db.CustomFieldValues.AsNoTracking()
                    .Any(v => v.FieldId == fieldId && v.Value != null
                              && EF.Functions.Like(v.Value, likePattern)
                              && v.TargetId == x.o.Id));
            }
        }

        var total = await q.CountAsync();
        var list = await q.OrderByDescending(x => x.o.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new WorkOrderListDto
            {
                Id = x.o.Id,
                OrderNo = x.o.OrderNo,
                ProductCode = x.ProductCode,
                ProductName = x.ProductName,
                Qty = x.o.Qty,
                PlanQty = x.o.Qty,
                Status = x.o.Status,
                DueDate = x.o.DueDate,
                CreatedAt = x.o.CreatedAt
            }).ToListAsync();

        await FillListProgressAsync(list, factoryId);
        await FillListExtAsync(list, factoryId);

        // 交期三态（列表逐条算；Calc 由 DueStateHelper 实现）
        var now = DateTime.Now;
        foreach (var item in list)
            item.DueState = DueStateHelper.Calc(item.Status, item.DueDate, now);

        return ApiResult<PageResult<WorkOrderListDto>>.Ok(new PageResult<WorkOrderListDto> { List = list, Total = total });
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
            throw ThrowHelper.Biz(nameof(QueryAsync), "自定义字段筛选值不能超过 256 个字符");

        if (!hasId)
        {
            if (value.Length > 0)
                throw ThrowHelper.Biz(nameof(QueryAsync), "请选择要筛选的自定义字段");
            return null;
        }

        var fieldId = query.CustomFieldId!.Value;
        var field = await _db.CustomFields.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fieldId && f.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(QueryAsync), "自定义字段不存在");
        if (field.Target != "work_order")
            throw ThrowHelper.Biz(nameof(QueryAsync), "只能按工单自定义字段筛选");

        var match = string.IsNullOrWhiteSpace(query.CustomFieldMatch)
            ? "exact"
            : query.CustomFieldMatch.Trim().ToLowerInvariant();
        if (match is not ("exact" or "contains"))
            throw ThrowHelper.Biz(nameof(QueryAsync), "匹配方式仅支持 exact 或 contains");

        if ((field.FieldType == "single" || field.FieldType == "number") && match != "exact")
            throw ThrowHelper.Biz(nameof(QueryAsync), "单选和数字字段仅支持精确匹配");

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
        return ApiResult<WorkOrderStatusCountsDto>.Ok(new WorkOrderStatusCountsDto
        {
            NotStarted = notStarted,
            Doing = doing,
            Done = done,
            Cancelled = cancelled,
            // 「全部」与 H5 一致：不含已取消
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
            throw ThrowHelper.Biz(nameof(GetByOrderNoAsync), "扫描失败：工单号为空");

        var order = await _db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrderNo == orderNo.Trim() && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(GetByOrderNoAsync), $"扫描失败：工单号 {orderNo} 不存在");
        // H5/扫码：传入当前用户以按报工权限过滤 tasks；AI/其它查进度不传则看全工序
        return ApiResult<WorkOrderDetailDto>.Ok(
            await BuildDetailAsync(order, reportPermissionUserId, reportPermissionRole));
    }

    private async Task<WorkOrderDetailDto> BuildDetailAsync(ProdWorkOrder order, long? filterUserId = null, byte? filterRole = null)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == order.ProductId)
            ?? throw ThrowHelper.Biz(nameof(BuildDetailAsync), "工单关联产品不存在");

        var ext = await _db.CustomFieldValues.AsNoTracking()
            .Where(v => v.TargetId == order.Id)
            .ToDictionaryAsync(v => v.FieldId, v => v.Value ?? "");

        var tasks = await LoadTasksAsync(order);
        if (filterUserId.HasValue && filterRole.HasValue)
        {
            var allowed = await ReportPermissionHelper.FilterAllowedOperationIdsAsync(
                _db, filterUserId.Value, filterRole.Value, tasks.Select(t => t.OperationId));
            tasks = tasks.Where(t => allowed.Contains(t.OperationId)).ToList();
        }

        return new WorkOrderDetailDto
        {
            Id = order.Id,
            OrderNo = order.OrderNo,
            ProductId = product.Id,
            ProductCode = product.Code,
            ProductName = product.Name,
            Qty = order.Qty,
            Status = order.Status,
            DueDate = order.DueDate,
            DueState = DueStateHelper.Calc(order.Status, order.DueDate, DateTime.Now),
            Ext = ext,
            Tasks = tasks
        };
    }

    private async Task<List<WorkOrderTaskDto>> LoadTasksAsync(ProdWorkOrder order)
    {
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
                             join op in _db.Operations.AsNoTracking() on s.OperationId equals op.Id
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
        var assigneeIds = tasks.Where(t => t.AssigneeUserId != null).Select(t => t.AssigneeUserId!.Value).Distinct().ToList();
        var assigneeNames = assigneeIds.Count > 0
            ? await _db.Users.AsNoTracking().Where(u => assigneeIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name)
            : new Dictionary<long, string>();
        return tasks.Select(t => new WorkOrderTaskDto
        {
            Id = t.Id,
            OperationId = t.OperationId,
            OperationName = names.GetValueOrDefault(t.OperationId, ""),
            Seq = t.Seq,
            PlanQty = t.PlanQty,
            DoneQty = goodMap.GetValueOrDefault(t.OperationId, 0),
            DefectQty = defectMap.GetValueOrDefault(t.OperationId, 0),
            AssigneeUserId = t.AssigneeUserId,
            AssigneeName = t.AssigneeUserId == null ? "" : assigneeNames.GetValueOrDefault(t.AssigneeUserId.Value, "")
        }).ToList();
    }

    public async Task<ApiResult<object?>> CreateAsync(WorkOrderCreateDto dto, long currentUserId, long factoryId)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == dto.ProductId && p.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(CreateAsync), "产品不存在");
        if (dto.Qty <= 0)
            throw ThrowHelper.Biz(nameof(CreateAsync), "数量必须大于0");

        var orderNo = await NextOrderNoAsync(dto.OrderNo, factoryId);

        var order = new ProdWorkOrder
        {
            FactoryId = factoryId,
            OrderNo = orderNo,
            ProductId = product.Id,
            Qty = dto.Qty,
            Status = 0,
            DueDate = dto.DueDate,
            CreatedBy = currentUserId,
            CreatedAt = DateTime.Now
        };
        _db.WorkOrders.Add(order);
        await _db.SaveChangesAsync();

        await SyncTasksAsync(order, product.RoutingId, dto.Qty, null);
        await SaveExtAsync(order.Id, dto.Ext);

        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> UpdateAsync(long id, WorkOrderUpdateDto dto, long factoryId)
    {
        var order = await _db.WorkOrders.FirstOrDefaultAsync(o => o.Id == id && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "工单不存在");

        if (order.Status == 3)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "工单已取消，不可修改");
        if (order.Status == 2)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "工单已结束，不可修改；如需修改请先「撤回」");

        // 工单号
        if (!string.IsNullOrWhiteSpace(dto.OrderNo))
        {
            var no = dto.OrderNo.Trim();
            if (await _db.WorkOrders.AnyAsync(o => o.FactoryId == factoryId && o.OrderNo == no && o.Id != id))
                throw ThrowHelper.Biz(nameof(UpdateAsync), "工单号不可重复");
            order.OrderNo = no;
        }

        // 计划交期（改交期无报工限制，可直接改；null 表示清空）
        order.DueDate = dto.DueDate;

        // 换产品
        if (dto.ProductId.HasValue && dto.ProductId.Value != order.ProductId)
        {
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == dto.ProductId.Value && p.FactoryId == factoryId)
                ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "产品不存在");
            var hasReport = await _db.Reports.AnyAsync(r => r.OrderId == id);
            if (hasReport)
                throw ThrowHelper.Biz(nameof(UpdateAsync), "该工单已有报工，不可更换产品");
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
                throw ThrowHelper.Biz(nameof(UpdateAsync), $"数量不能小于已报良品数 {totalGood}");

            order.Qty = dto.Qty.Value;
            // 无报工时同步各任务计划数；有报工仅改工单级 qty
            if (!hasReport)
                await SyncTasksAsync(order, null, dto.Qty.Value, null);
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
                    throw ThrowHelper.Biz(nameof(UpdateAsync), $"工序计划数不能小于该工序已报良品数 {done}");
                task.PlanQty = plan.PlanQty;
            }
        }

        await _db.SaveChangesAsync();

        if (dto.Ext != null)
            await SaveExtAsync(id, dto.Ext);

        await RecalcStatusAsync(order);
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> TransitionAsync(long id, string action, long factoryId)
    {
        var order = await _db.WorkOrders.FirstOrDefaultAsync(o => o.Id == id && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(TransitionAsync), "工单不存在");

        switch (action)
        {
            case "start":
                if (order.Status != 0) throw ThrowHelper.Biz(nameof(TransitionAsync), "仅未开始工单可开始");
                order.Status = 1;
                break;
            case "finish":
                if (order.Status != 1) throw ThrowHelper.Biz(nameof(TransitionAsync), "仅执行中工单可结束");
                order.Status = 2;
                break;
            case "withdraw":
                if (order.Status == 2) order.Status = 1;
                else if (order.Status == 1) order.Status = 0;
                else throw ThrowHelper.Biz(nameof(TransitionAsync), "当前状态不可撤回");
                break;
            case "cancel":
                if (order.Status == 2 || order.Status == 3) throw ThrowHelper.Biz(nameof(TransitionAsync), "当前状态不可取消");
                order.Status = 3;
                break;
            case "restore":
                if (order.Status != 3) throw ThrowHelper.Biz(nameof(TransitionAsync), "仅已取消工单可恢复");
                order.Status = 0;
                break;
            default:
                throw ThrowHelper.Biz(nameof(TransitionAsync), $"未知操作: {action}");
        }

        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> CopyAsync(long id, long factoryId)
    {
        var src = await _db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(CopyAsync), "工单不存在");

        var orderNo = await NextOrderNoAsync(null, factoryId);
        var order = new ProdWorkOrder
        {
            FactoryId = factoryId,
            OrderNo = orderNo,
            ProductId = src.ProductId,
            Qty = src.Qty,
            Status = 0,
            // 注意：不复制交期（DueDate 保持 null，交期是新单的事）
            CreatedBy = src.CreatedBy,
            CreatedAt = DateTime.Now
        };
        _db.WorkOrders.Add(order);
        await _db.SaveChangesAsync();

        var srcTasks = await _db.WorkOrderOperations.AsNoTracking().Where(t => t.WorkOrderId == id).ToListAsync();
        foreach (var t in srcTasks)
            _db.WorkOrderOperations.Add(new ProdWorkOrderOperation
            {
                WorkOrderId = order.Id,
                OperationId = t.OperationId,
                Seq = t.Seq,
                PlanQty = t.PlanQty
            });

        var srcExt = await _db.CustomFieldValues.AsNoTracking().Where(v => v.TargetId == id).ToListAsync();
        foreach (var v in srcExt)
            _db.CustomFieldValues.Add(new SysCustomFieldValue
            {
                FieldId = v.FieldId,
                TargetId = order.Id,
                Value = v.Value
            });

        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> DeleteAsync(long id)
    {
        var order = await _db.WorkOrders.FindAsync(id)
            ?? throw ThrowHelper.Biz(nameof(DeleteAsync), "工单不存在");

        _db.Reports.RemoveRange(_db.Reports.Where(r => r.OrderId == id));
        _db.WorkOrderOperations.RemoveRange(_db.WorkOrderOperations.Where(t => t.WorkOrderId == id));
        _db.CustomFieldValues.RemoveRange(_db.CustomFieldValues.Where(v => v.TargetId == id));
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
            throw ThrowHelper.Biz(nameof(NextOrderNoAsync), "自动单号用尽，请改用自定义单号");
        return ((char)(letter + 1)) + "0001";
    }

    /// <summary>
    /// 重建工单工序任务。routingId 为 null 时沿用现有（用于只改计划数的场景）。
    /// </summary>
    private async Task SyncTasksAsync(ProdWorkOrder order, long? routingId, int planQty, List<WorkOrderPlanDto>? plans)
    {
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

    /// <summary>按工序任务重算工单完成状态。注意：本方法不修改未开始(0)/已取消(3)。</summary>
    public async Task RecalcStatusAsync(ProdWorkOrder order)
    {
        if (order.Status == 3) return;

        var hasReport = await _db.Reports.AnyAsync(r => r.OrderId == order.Id && r.ReviewStatus != 2);
        if (!hasReport)
        {
            if (order.Status == 1) order.Status = 0; // 无报工回到未开始
            await _db.SaveChangesAsync();
            return;
        }

        order.Status = 1;

        var tasks = await _db.WorkOrderOperations.Where(t => t.WorkOrderId == order.Id).ToListAsync();
        if (tasks.Count == 0)
        {
            // 旧数据：无任务行，按工单级 qty 判完成
            var totalGood = await _db.Reports
                .Where(r => r.OrderId == order.Id && r.ReviewStatus != 2)
                .SumAsync(r => (int?)r.GoodQty) ?? 0;
            if (totalGood >= order.Qty) order.Status = 2;
        }
        else
        {
            var reportSum = await (from r in _db.Reports
                                   where r.OrderId == order.Id && r.ReviewStatus != 2
                                   group r by r.OperationId into g
                                   select new { OperationId = g.Key, Done = g.Sum(x => x.GoodQty) })
                                   .ToDictionaryAsync(x => x.OperationId, x => x.Done);
            var allDone = tasks.All(t => reportSum.GetValueOrDefault(t.OperationId, 0) >= t.PlanQty);
            if (allDone) order.Status = 2;
        }

        await _db.SaveChangesAsync();
    }
}
