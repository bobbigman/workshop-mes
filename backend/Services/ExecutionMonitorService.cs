using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;

namespace ahu.MicrosoftMes.Services;

public interface IExecutionMonitorService
{
    Task<ApiResult<ExecutionMonitorDto>> GetAsync(ExecutionMonitorQueryDto query, long factoryId, long userId, byte role);
}

public class ExecutionMonitorQueryDto
{
    public string? Keyword { get; set; }
    public byte? Status { get; set; }
    public DateTime? DueFrom { get; set; }
    public DateTime? DueTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class ExecutionMonitorDto
{
    public DateTime GeneratedAt { get; set; }
    public ExecutionMonitorSummaryDto Summary { get; set; } = new();
    public PageResult<ExecutionMonitorOrderDto> Orders { get; set; } = new();
    public PageResult<AbnormalListDto> Abnormals { get; set; } = new();
    public PageResult<ExecutionMonitorOverdueDto> OverdueOrders { get; set; } = new();
    public ExecutionMonitorTodosDto Todos { get; set; } = new();
}

public class ExecutionMonitorSummaryDto
{
    public int NotStarted { get; set; }
    public int Doing { get; set; }
    public int Done { get; set; }
    public int DueWarning { get; set; }
    public int DueOverdue { get; set; }
    public int OpenAbnormal { get; set; }
    public string ScopeNote { get; set; } = "按关键词与交期筛选后的全集统计（不含已取消；忽略当前状态筛选）";
}

public class ExecutionMonitorOrderDto
{
    public long Id { get; set; }
    public string OrderNo { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int Qty { get; set; }
    public int DoneQty { get; set; }
    public int RemainQty { get; set; }
    public byte Status { get; set; }
    public DateTime? DueDate { get; set; }
    public string DueState { get; set; } = "normal";
    public int? ProgressPercent { get; set; }
    public string? ProgressHint { get; set; }
    public bool HasOpenAbnormal { get; set; }
    public List<ExecutionMonitorOpDto> Ops { get; set; } = new();
}

public class ExecutionMonitorOpDto
{
    public long? TaskId { get; set; }
    public long OperationId { get; set; }
    public int Seq { get; set; }
    public string OperationName { get; set; } = "";
    public int PlanQty { get; set; }
    public int DoneQty { get; set; }
    public int DefectQty { get; set; }
    public long? AssigneeUserId { get; set; }
    public string AssigneeName { get; set; } = "";
    public string Status { get; set; } = "";
    public bool IsVirtual { get; set; }
    public bool CanAssign { get; set; }
}

public class ExecutionMonitorOverdueDto
{
    public long Id { get; set; }
    public string OrderNo { get; set; } = "";
    public string ProductName { get; set; } = "";
    public DateTime? DueDate { get; set; }
    public byte Status { get; set; }
    public int? ProgressPercent { get; set; }
}

public class ExecutionMonitorTodosDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PendingReviewCount { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? UnassignedOpCount { get; set; }
    public string ScopeNote { get; set; } = "当前权限范围，不随工单筛选";
}

public class ExecutionMonitorService : IExecutionMonitorService
{
    private const int MaxPageSize = 100;
    private const int SideListTake = 10;

    private readonly AppDbContext _db;

    public ExecutionMonitorService(AppDbContext db) { _db = db; }

    public async Task<ApiResult<ExecutionMonitorDto>> GetAsync(
        ExecutionMonitorQueryDto query, long factoryId, long userId, byte role)
    {
        NormalizeQuery(query);
        var now = DateTime.Now;

        // 轻量行拉到内存：排序依赖 DueStateHelper / 异常集合，避免 EF 翻译失败
        var light = await (
            from o in _db.WorkOrders.AsNoTracking()
            where o.FactoryId == factoryId && o.Status != 3
            join p in _db.Products.AsNoTracking() on o.ProductId equals p.Id
            select new LightOrder
            {
                Id = o.Id,
                OrderNo = o.OrderNo,
                ProductCode = p.Code,
                ProductName = p.Name,
                Qty = o.Qty,
                ProductId = o.ProductId,
                Status = o.Status,
                DueDate = o.DueDate
            }).ToListAsync();

        light = ApplyKeyword(light, query.Keyword);
        light = ApplyDueRange(light, query.DueFrom, query.DueTo);

        var openAbnormalOrderIds = await _db.Abnormals.AsNoTracking()
            .Where(a => a.FactoryId == factoryId && a.Status == 0 && a.WorkOrderId != null)
            .Select(a => a.WorkOrderId!.Value)
            .Distinct()
            .ToListAsync();
        var abnormalSet = openAbnormalOrderIds.ToHashSet();

        var summary = new ExecutionMonitorSummaryDto
        {
            NotStarted = light.Count(x => x.Status == 0),
            Doing = light.Count(x => x.Status == 1),
            Done = light.Count(x => x.Status == 2),
            DueWarning = light.Count(x => DueStateHelper.Calc(x.Status, x.DueDate, now) == DueState.Warning),
            DueOverdue = light.Count(x => DueStateHelper.Calc(x.Status, x.DueDate, now) == DueState.Overdue),
            OpenAbnormal = await _db.Abnormals.AsNoTracking()
                .CountAsync(a => a.FactoryId == factoryId && a.Status == 0)
        };

        var filtered = query.Status.HasValue
            ? light.Where(x => x.Status == query.Status.Value).ToList()
            : light;

        var sorted = filtered
            .OrderBy(x => x.Status >= 2 ? 1 : 0)
            .ThenByDescending(x => DueStateHelper.Calc(x.Status, x.DueDate, now) == DueState.Overdue)
            .ThenByDescending(x => abnormalSet.Contains(x.Id))
            .ThenBy(x => x.DueDate == null)
            .ThenBy(x => x.DueDate)
            .ThenBy(x => x.Id)
            .ToList();

        var total = sorted.Count;
        var pageSize = query.PageSize;
        var maxPage = total == 0 ? 1 : (int)Math.Ceiling(total / (double)pageSize);
        var page = query.Page > maxPage ? maxPage : query.Page;
        var pageRows = sorted.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var bundles = await WorkOrderProgressQuery.LoadAsync(
            _db, factoryId,
            pageRows.Select(o => new WorkOrderProgressQuery.OrderKey { Id = o.Id, ProductId = o.ProductId, Qty = o.Qty }).ToList(),
            nameof(GetAsync));

        var orderDtos = pageRows.Select(o =>
        {
            var b = bundles[o.Id];
            return new ExecutionMonitorOrderDto
            {
                Id = o.Id,
                OrderNo = o.OrderNo,
                ProductCode = o.ProductCode,
                ProductName = o.ProductName,
                Qty = o.Qty,
                DoneQty = b.Progress.DoneQty,
                RemainQty = b.Progress.RemainQty,
                Status = o.Status,
                DueDate = o.DueDate,
                DueState = DueStateHelper.Calc(o.Status, o.DueDate, now),
                ProgressPercent = b.Progress.ProgressPercent,
                ProgressHint = b.Progress.ProgressHint,
                HasOpenAbnormal = abnormalSet.Contains(o.Id),
                Ops = b.Ops.Select(op => new ExecutionMonitorOpDto
                {
                    TaskId = op.TaskId,
                    OperationId = op.OperationId,
                    Seq = op.Seq,
                    OperationName = op.OperationName,
                    PlanQty = op.PlanQty,
                    DoneQty = op.DoneQty,
                    DefectQty = op.DefectQty,
                    AssigneeUserId = op.AssigneeUserId,
                    AssigneeName = op.AssigneeName,
                    Status = op.Status,
                    IsVirtual = op.IsVirtual,
                    CanAssign = op.CanAssign && !op.IsVirtual && op.TaskId.HasValue
                }).ToList()
            };
        }).ToList();

        var overdueAll = light
            .Where(x => DueStateHelper.Calc(x.Status, x.DueDate, now) == DueState.Overdue)
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.Id)
            .ToList();
        var overduePage = overdueAll.Take(SideListTake).ToList();
        var overdueBundles = await WorkOrderProgressQuery.LoadAsync(
            _db, factoryId,
            overduePage.Select(o => new WorkOrderProgressQuery.OrderKey { Id = o.Id, ProductId = o.ProductId, Qty = o.Qty }).ToList(),
            nameof(GetAsync));
        var overdueList = overduePage.Select(o => new ExecutionMonitorOverdueDto
        {
            Id = o.Id,
            OrderNo = o.OrderNo,
            ProductName = o.ProductName,
            DueDate = o.DueDate,
            Status = o.Status,
            ProgressPercent = overdueBundles[o.Id].Progress.ProgressPercent
        }).ToList();

        var abnormalList = await LoadOpenAbnormalsAsync(factoryId, SideListTake);
        var todos = await BuildTodosAsync(factoryId, userId, role);

        return ApiResult<ExecutionMonitorDto>.Ok(new ExecutionMonitorDto
        {
            GeneratedAt = now,
            Summary = summary,
            Orders = new PageResult<ExecutionMonitorOrderDto> { List = orderDtos, Total = total },
            Abnormals = new PageResult<AbnormalListDto> { List = abnormalList, Total = summary.OpenAbnormal },
            OverdueOrders = new PageResult<ExecutionMonitorOverdueDto> { List = overdueList, Total = overdueAll.Count },
            Todos = todos
        });
    }

    private async Task<List<AbnormalListDto>> LoadOpenAbnormalsAsync(long factoryId, int take)
    {
        var rows = await (
            from a in _db.Abnormals.AsNoTracking()
            where a.FactoryId == factoryId && a.Status == 0
            join u in _db.Users.AsNoTracking() on a.ReportedBy equals u.Id into uj
            from u in uj.DefaultIfEmpty()
            join o in _db.WorkOrders.AsNoTracking() on a.WorkOrderId equals o.Id into oj
            from o in oj.DefaultIfEmpty()
            orderby a.ReportedAt descending
            select new AbnormalListDto
            {
                Id = a.Id,
                AbnormalType = a.AbnormalType,
                Description = a.Description,
                ImagePath = a.ImagePath,
                WorkOrderId = a.WorkOrderId,
                OrderNo = o != null ? o.OrderNo : null,
                ReportedBy = a.ReportedBy,
                ReporterName = u != null ? u.Name : "",
                ReportedAt = a.ReportedAt,
                Status = a.Status,
                HandledBy = a.HandledBy,
                RecoveredAt = a.RecoveredAt,
                HandleNote = a.HandleNote
            }).Take(take).ToListAsync();

        foreach (var r in rows)
            r.TypeLabel = AbnormalService.TypeLabel(r.AbnormalType);
        return rows;
    }

    private async Task<ExecutionMonitorTodosDto> BuildTodosAsync(long factoryId, long userId, byte role)
    {
        var todos = new ExecutionMonitorTodosDto();
        var canReview = role == 1 || role == 3;
        var canAssign = role == 1 || role == 3;

        if (canReview)
        {
            var q = _db.Reports.AsNoTracking()
                .Where(r => r.FactoryId == factoryId && r.ReviewStatus == 0);
            if (role == 3)
            {
                var myDeptIds = await _db.DepartmentUsers.AsNoTracking()
                    .Where(x => x.UserId == userId)
                    .Select(x => x.DepartmentId).ToListAsync();
                var allowedUserIds = await _db.DepartmentUsers.AsNoTracking()
                    .Where(x => myDeptIds.Contains(x.DepartmentId))
                    .Select(x => x.UserId).Distinct().ToListAsync();
                q = q.Where(r => allowedUserIds.Contains(r.UserId));
            }
            todos.PendingReviewCount = await q.CountAsync();
        }

        if (canAssign)
        {
            // 未派工工序：未开始/执行中、真实任务行、无派工明细且冗余列空、正计划且尚未报满
            var assignedTaskIds = await _db.WorkOrderOperationAssignees.AsNoTracking()
                .Select(a => a.WorkOrderOperationId).Distinct().ToListAsync();
            var candidates = await (
                from t in _db.WorkOrderOperations.AsNoTracking()
                where t.PlanQty > 0
                      && t.AssigneeUserId == null
                      && !assignedTaskIds.Contains(t.Id)
                join o in _db.WorkOrders.AsNoTracking() on t.WorkOrderId equals o.Id
                where o.FactoryId == factoryId && (o.Status == 0 || o.Status == 1)
                select new { t.WorkOrderId, t.OperationId, t.PlanQty }
            ).ToListAsync();

            if (candidates.Count == 0)
            {
                todos.UnassignedOpCount = 0;
            }
            else
            {
                var orderIds = candidates.Select(c => c.WorkOrderId).Distinct().ToList();
                var doneRows = await (
                    from r in _db.Reports.AsNoTracking()
                    where orderIds.Contains(r.OrderId) && r.ReviewStatus != 2
                    group r by new { r.OrderId, r.OperationId } into g
                    select new { g.Key.OrderId, g.Key.OperationId, Done = g.Sum(x => x.GoodQty) }
                ).ToListAsync();
                var doneMap = doneRows.ToDictionary(x => (x.OrderId, x.OperationId), x => x.Done);
                todos.UnassignedOpCount = candidates.Count(c =>
                    doneMap.GetValueOrDefault((c.WorkOrderId, c.OperationId), 0) < c.PlanQty);
            }
        }

        return todos;
    }

    private static void NormalizeQuery(ExecutionMonitorQueryDto query)
    {
        if (query.Page < 1) query.Page = 1;
        if (query.PageSize < 1) query.PageSize = 20;
        if (query.PageSize > MaxPageSize) query.PageSize = MaxPageSize;
        if (query.Status.HasValue && query.Status.Value > 2)
            throw ThrowHelper.Biz(nameof(NormalizeQuery), "状态筛选仅支持未开始/执行中/已结束");
        if (query.DueFrom.HasValue && query.DueTo.HasValue && query.DueFrom.Value.Date > query.DueTo.Value.Date)
            throw ThrowHelper.Biz(nameof(NormalizeQuery), "交期起止日期范围无效");
        if (query.Keyword != null)
            query.Keyword = query.Keyword.Trim();
    }

    private static List<LightOrder> ApplyKeyword(List<LightOrder> list, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return list;
        return list.Where(x =>
            x.OrderNo.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || x.ProductCode.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || x.ProductName.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// 无交期范围：包含未设交期；指定范围：排除未设交期；结束日按次日零点前。
    /// </summary>
    private static List<LightOrder> ApplyDueRange(List<LightOrder> list, DateTime? dueFrom, DateTime? dueTo)
    {
        if (!dueFrom.HasValue && !dueTo.HasValue)
            return list;

        DateTime? from = dueFrom?.Date;
        DateTime? toExclusive = dueTo.HasValue ? dueTo.Value.Date.AddDays(1) : null;

        return list.Where(x =>
        {
            if (x.DueDate == null) return false;
            if (from.HasValue && x.DueDate < from.Value) return false;
            if (toExclusive.HasValue && x.DueDate >= toExclusive.Value) return false;
            return true;
        }).ToList();
    }

    private sealed class LightOrder
    {
        public long Id { get; set; }
        public string OrderNo { get; set; } = "";
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public int Qty { get; set; }
        public long ProductId { get; set; }
        public byte Status { get; set; }
        public DateTime? DueDate { get; set; }
    }
}
