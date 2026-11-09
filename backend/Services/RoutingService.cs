using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IRoutingService
{
    Task<ApiResult<PageResult<RoutingListDto>>> QueryAsync(RoutingQueryDto query, long factoryId);
    Task<ApiResult<RoutingDetailDto>> GetAsync(long id, long factoryId);
    Task<ApiResult<object?>> CreateAsync(RoutingCreateDto dto, long factoryId);
    Task<ApiResult<object?>> UpdateAsync(long id, RoutingCreateDto dto, long factoryId);
    Task<ApiResult<object?>> DeleteAsync(long id);
    Task<ApiResult<object?>> ForbidAsync(long id, long factoryId, long userId);
    Task<ApiResult<object?>> UnforbidAsync(long id, long factoryId, long userId);
    Task<ApiResult<object?>> AuditAsync(long id, long factoryId, long userId);
    Task<ApiResult<object?>> UnauditAsync(long id, long factoryId, long userId);
}

public class RoutingQueryDto
{
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public byte? Status { get; set; }
    public bool IncludeDisabled { get; set; }
    public bool IncludeUnaudited { get; set; }
    public string? FilterField { get; set; }
    public string? FilterOp { get; set; }
    public string? FilterValue { get; set; }
}

public class RoutingStepDto
{
    public long OperationId { get; set; }
    public int Seq { get; set; }
}

public class RoutingCreateDto
{
    public long? CopyFromId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public List<RoutingStepDto> Steps { get; set; } = new();
}

public class RoutingListDto
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string StepNames { get; set; } = "";
    public byte Status { get; set; }
    public byte AuditStatus { get; set; }
}

public class RoutingDetailDto
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public List<RoutingStepDto> Steps { get; set; } = new();
    public byte Status { get; set; }
    public byte AuditStatus { get; set; }
    public long? AuditedBy { get; set; }
    public string? AuditedByName { get; set; }
    public DateTime? AuditedAt { get; set; }
    public long? ForbidBy { get; set; }
    public string? ForbidByName { get; set; }
    public DateTime? ForbidAt { get; set; }
    public long? UnforbidBy { get; set; }
    public string? UnforbidByName { get; set; }
    public DateTime? UnforbidAt { get; set; }
}

public class RoutingService : IRoutingService
{
    private readonly AppDbContext _db;
    public RoutingService(AppDbContext db) { _db = db; }

    public async Task<ApiResult<PageResult<RoutingListDto>>> QueryAsync(RoutingQueryDto query, long factoryId)
    {
        var q = _db.Routings.AsNoTracking().Where(r => r.FactoryId == factoryId);
        var fieldKey = string.IsNullOrWhiteSpace(query.FilterField) ? "" : query.FilterField.Trim();
        var filterIsStatus = fieldKey.Equals("status", StringComparison.OrdinalIgnoreCase);
        var filterIsAudit = fieldKey.Equals("auditStatus", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(query.FilterField))
            q = ApplyQuickFilter(q, query);
        else if (!string.IsNullOrWhiteSpace(query.Keyword))
            q = q.Where(r => r.Code.Contains(query.Keyword) || r.Name.Contains(query.Keyword));

        if (!filterIsStatus)
        {
            if (query.Status.HasValue) q = q.Where(r => r.Status == query.Status.Value);
            else if (!query.IncludeDisabled) q = q.Where(r => r.Status == 1);
        }
        if (!filterIsAudit && !query.IncludeUnaudited)
            q = q.Where(r => r.AuditStatus == 1);

        var total = await q.CountAsync();
        var routings = await q.OrderByDescending(r => r.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync();

        var rids = routings.Select(r => r.Id).ToList();
        var steps = await _db.RoutingSteps.AsNoTracking()
            .Where(s => rids.Contains(s.RoutingId)).OrderBy(s => s.Seq).ToListAsync();
        var ops = await _db.Operations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name);

        var list = routings.Select(r => new RoutingListDto
        {
            Id = r.Id,
            Code = r.Code,
            Name = r.Name,
            Status = r.Status,
            AuditStatus = r.AuditStatus,
            StepNames = string.Join(" → ", steps.Where(s => s.RoutingId == r.Id)
                .Select(s => ops.GetValueOrDefault(s.OperationId, "")))
        }).ToList();

        return ApiResult<PageResult<RoutingListDto>>.Ok(new PageResult<RoutingListDto> { List = list, Total = total });
    }

    private static IQueryable<BaseRouting> ApplyQuickFilter(IQueryable<BaseRouting> q, RoutingQueryDto query)
    {
        var field = query.FilterField!.Trim();
        var value = string.IsNullOrWhiteSpace(query.FilterValue) ? "" : query.FilterValue.Trim();
        if (value.Length > 256)
            throw ThrowHelper.Biz(nameof(QueryAsync), "筛选值太长，最多 256 个字符，请缩短后重试。");
        var op = string.IsNullOrWhiteSpace(query.FilterOp) ? "" : query.FilterOp.Trim().ToLowerInvariant();
        if (value.Length == 0) return q;
        if (string.IsNullOrEmpty(op))
            op = field is "status" or "auditStatus" ? "eq" : "contains";
        if (op is not ("eq" or "contains"))
            throw ThrowHelper.Biz(nameof(QueryAsync), "比较方式只支持：等于、包含。");

        switch (field)
        {
            case "keyword":
                return op == "eq"
                    ? q.Where(x => x.Code == value || x.Name == value)
                    : q.Where(x => x.Code.Contains(value) || x.Name.Contains(value));
            case "code":
                return op == "eq" ? q.Where(x => x.Code == value) : q.Where(x => x.Code.Contains(value));
            case "name":
                return op == "eq" ? q.Where(x => x.Name == value) : q.Where(x => x.Name.Contains(value));
            case "status":
                if (value is "all" or "全部") return q;
                if (!byte.TryParse(value, out var st) || st > 1)
                    throw ThrowHelper.Biz(nameof(QueryAsync), "启用状态请选择「启用」或「禁用」。");
                return q.Where(x => x.Status == st);
            case "auditStatus":
                if (value is "all" or "全部") return q;
                if (!byte.TryParse(value, out var audit) || audit > 1)
                    throw ThrowHelper.Biz(nameof(QueryAsync), "审核状态请选择「未审核」或「已审核」。");
                return q.Where(x => x.AuditStatus == audit);
            default:
                throw ThrowHelper.Biz(nameof(QueryAsync), $"不支持的筛选字段「{field}」。");
        }
    }

    public async Task<ApiResult<RoutingDetailDto>> GetAsync(long id, long factoryId)
    {
        var r = await _db.Routings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(GetAsync), "指定的工艺路线在系统中不存在。请到「工艺路线」列表核对编号是否正确。");
        var steps = await _db.RoutingSteps.AsNoTracking()
            .Where(s => s.RoutingId == id).OrderBy(s => s.Seq)
            .Select(s => new RoutingStepDto { OperationId = s.OperationId, Seq = s.Seq }).ToListAsync();
        var userIds = new[] { r.AuditedBy, r.ForbidBy, r.UnforbidBy }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var names = userIds.Count == 0
            ? new Dictionary<long, string>()
            : await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name);
        return ApiResult<RoutingDetailDto>.Ok(new RoutingDetailDto
        {
            Id = r.Id,
            Code = r.Code,
            Name = r.Name,
            Steps = steps,
            Status = r.Status,
            AuditStatus = r.AuditStatus,
            AuditedBy = r.AuditedBy,
            AuditedByName = r.AuditedBy.HasValue ? names.GetValueOrDefault(r.AuditedBy.Value) : null,
            AuditedAt = r.AuditedAt,
            ForbidBy = r.ForbidBy,
            ForbidByName = r.ForbidBy.HasValue ? names.GetValueOrDefault(r.ForbidBy.Value) : null,
            ForbidAt = r.ForbidAt,
            UnforbidBy = r.UnforbidBy,
            UnforbidByName = r.UnforbidBy.HasValue ? names.GetValueOrDefault(r.UnforbidBy.Value) : null,
            UnforbidAt = r.UnforbidAt
        });
    }

    public async Task<ApiResult<object?>> CreateAsync(RoutingCreateDto dto, long factoryId)
    {
        await using var ownedTx = _db.Database.CurrentTransaction == null ? await _db.Database.BeginTransactionAsync() : null;
        if (dto.CopyFromId.HasValue && !await _db.Routings.AnyAsync(r => r.Id == dto.CopyFromId && r.FactoryId == factoryId))
            throw ThrowHelper.Biz(nameof(CreateAsync), "复制工艺路线时指定的来源路线不存在或不属于本厂。请到「工艺路线」确认来源路线编号正确且归属本厂。");
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Name))
            throw ThrowHelper.Biz(nameof(CreateAsync), "工艺路线的编号和名称都必须填写。请填写完整的路线编号和名称后再保存。");
        if (dto.Steps == null || dto.Steps.Count == 0)
            throw ThrowHelper.Biz(nameof(CreateAsync), "工艺路线至少要包含一道工序。请在路线中添加至少一个工序步骤后再保存。");
        await ValidateStepsAsync(dto.Steps, factoryId);

        var routing = new BaseRouting
        {
            FactoryId = factoryId,
            Code = dto.Code.Trim(),
            Name = dto.Name.Trim(),
            Status = 1,
            AuditStatus = 0,
            CreatedAt = DateTime.Now
        };
        _db.Routings.Add(routing);
        await _db.SaveChangesAsync();
        await SaveSteps(routing.Id, dto.Steps);
        if (ownedTx != null) await ownedTx.CommitAsync();
        return ApiResult<object?>.Ok(routing.Id);
    }

    private async Task ValidateStepsAsync(List<RoutingStepDto> steps, long factoryId)
    {
        var ids = steps.Select(s => s.OperationId).Distinct().ToList();
        if (ids.Count != steps.Count)
            throw ThrowHelper.Biz(nameof(ValidateStepsAsync), "同一条工艺路线中不能重复添加同一道工序。请检查工序列表，移除重复工序条目。");
        var ops = await _db.Operations.AsNoTracking()
            .Where(o => o.FactoryId == factoryId && ids.Contains(o.Id)).ToListAsync();
        if (ops.Count != ids.Count)
            throw ThrowHelper.Biz(nameof(ValidateStepsAsync), "路线中添加的某道工序不存在或不属于本厂。请到「工序」确认该工序已创建且归属本厂，再回路线编辑页修正。");
        var bad = ops.FirstOrDefault(o => o.Status != 1 || o.AuditStatus != 1);
        if (bad != null)
            throw ThrowHelper.Biz(nameof(ValidateStepsAsync),
                $"工序「{bad.Code} {bad.Name}」未审核或已禁用，不能加入工艺路线。请先到「工序」审核并启用。");
    }

    public async Task<ApiResult<object?>> UpdateAsync(long id, RoutingCreateDto dto, long factoryId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var routing = await _db.Routings.FirstOrDefaultAsync(r => r.Id == id && r.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "工艺路线不存在");
        if (routing.AuditStatus == 1)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "已审核的工艺路线不能直接改。请先点「反审核」，改完再审核。");
        if (dto.Steps == null || dto.Steps.Count == 0)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "请至少添加一道工序");
        await ValidateStepsAsync(dto.Steps, factoryId);

        routing.Code = dto.Code.Trim();
        routing.Name = dto.Name.Trim();
        _db.RoutingSteps.RemoveRange(_db.RoutingSteps.Where(s => s.RoutingId == id));
        await _db.SaveChangesAsync();
        await SaveSteps(id, dto.Steps);
        await tx.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }

    private async Task SaveSteps(long routingId, List<RoutingStepDto> steps)
    {
        var seq = 1;
        foreach (var s in steps)
        {
            _db.RoutingSteps.Add(new BaseRoutingStep
            {
                RoutingId = routingId,
                OperationId = s.OperationId,
                Seq = seq++
            });
        }
        await _db.SaveChangesAsync();
    }

    public async Task<ApiResult<object?>> DeleteAsync(long id)
    {
        var msg = await DeleteGuard.CheckAsync(_db, "Routing", id);
        if (msg != null) throw ThrowHelper.Biz(nameof(DeleteAsync), msg);

        var routing = await _db.Routings.FindAsync(id)
            ?? throw ThrowHelper.Biz(nameof(DeleteAsync), "工艺路线不存在");
        if (routing.AuditStatus == 1)
            throw ThrowHelper.Biz(nameof(DeleteAsync), "已审核的工艺路线不能直接删。请先反审核，确认无产品引用后再删。");
        _db.RoutingSteps.RemoveRange(_db.RoutingSteps.Where(s => s.RoutingId == id));
        _db.Routings.Remove(routing);
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> ForbidAsync(long id, long factoryId, long userId)
    {
        var routing = await _db.Routings.FirstOrDefaultAsync(r => r.Id == id && r.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(ForbidAsync), "工艺路线不存在");
        if (routing.Status == 0)
            throw ThrowHelper.Biz(nameof(ForbidAsync), "该工艺路线已经是禁用状态，无需再禁用。");
        routing.Status = 0;
        routing.ForbidBy = userId;
        routing.ForbidAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return new ApiResult<object?> { Code = 0, Msg = "已禁用；新产品不能再选这条工艺路线。" };
    }

    public async Task<ApiResult<object?>> UnforbidAsync(long id, long factoryId, long userId)
    {
        var routing = await _db.Routings.FirstOrDefaultAsync(r => r.Id == id && r.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UnforbidAsync), "工艺路线不存在");
        if (routing.Status == 1)
            throw ThrowHelper.Biz(nameof(UnforbidAsync), "该工艺路线已经是启用状态，无需再反禁用。");
        routing.Status = 1;
        routing.UnforbidBy = userId;
        routing.UnforbidAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return new ApiResult<object?> { Code = 0, Msg = "已反禁用；若已审核则可再次用于产品。" };
    }

    public async Task<ApiResult<object?>> AuditAsync(long id, long factoryId, long userId)
    {
        var routing = await _db.Routings.FirstOrDefaultAsync(r => r.Id == id && r.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(AuditAsync), "工艺路线不存在");
        if (routing.AuditStatus == 1)
            throw ThrowHelper.Biz(nameof(AuditAsync), "该工艺路线已经审核过了，无需再审。");
        var stepOpIds = await _db.RoutingSteps.AsNoTracking()
            .Where(s => s.RoutingId == id).Select(s => s.OperationId).ToListAsync();
        if (stepOpIds.Count > 0)
        {
            var bad = await _db.Operations.AsNoTracking()
                .Where(o => stepOpIds.Contains(o.Id) && (o.Status != 1 || o.AuditStatus != 1))
                .Select(o => o.Code + " " + o.Name).FirstOrDefaultAsync();
            if (bad != null)
                throw ThrowHelper.Biz(nameof(AuditAsync), $"路线里的工序「{bad}」未审核或已禁用，请先处理工序后再审核路线。");
        }
        routing.AuditStatus = 1;
        routing.AuditedBy = userId;
        routing.AuditedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return new ApiResult<object?> { Code = 0, Msg = "已审核；启用状态下可挂到产品上。" };
    }

    public async Task<ApiResult<object?>> UnauditAsync(long id, long factoryId, long userId)
    {
        var routing = await _db.Routings.FirstOrDefaultAsync(r => r.Id == id && r.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UnauditAsync), "工艺路线不存在");
        if (routing.AuditStatus != 1)
            throw ThrowHelper.Biz(nameof(UnauditAsync), "当前不需要反审核。");
        routing.AuditStatus = 0;
        _ = userId;
        await _db.SaveChangesAsync();
        return new ApiResult<object?> { Code = 0, Msg = "已反审核；请改完后再审核。" };
    }
}
