using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IOperationService
{
    Task<ApiResult<PageResult<OperationListDto>>> QueryAsync(OperationQueryDto query, long factoryId);
    Task<ApiResult<OperationDetailDto>> GetAsync(long id, long factoryId);
    Task<ApiResult<object?>> CreateAsync(OperationCreateDto dto, long factoryId);
    Task<ApiResult<object?>> UpdateAsync(long id, OperationCreateDto dto, long factoryId);
    Task<ApiResult<object?>> DeleteAsync(long id);
    Task<ApiResult<object?>> ForbidAsync(long id, long factoryId, long userId);
    Task<ApiResult<object?>> UnforbidAsync(long id, long factoryId, long userId);
    Task<ApiResult<object?>> AuditAsync(long id, long factoryId, long userId);
    Task<ApiResult<object?>> UnauditAsync(long id, long factoryId, long userId);
}

public class OperationQueryDto
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

public class OperationCreateDto
{
    public long? CopyFromId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public List<long> DeptIds { get; set; } = new();
    public List<long> DefectIds { get; set; } = new();
}

public class OperationListDto
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string DeptNames { get; set; } = "";
    public string DefectNames { get; set; } = "";
    public byte Status { get; set; }
    public byte AuditStatus { get; set; }
}

public class OperationDetailDto
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public List<long> DeptIds { get; set; } = new();
    public List<long> DefectIds { get; set; } = new();
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

public class OperationService : IOperationService
{
    private readonly AppDbContext _db;
    public OperationService(AppDbContext db) { _db = db; }

    public async Task<ApiResult<PageResult<OperationListDto>>> QueryAsync(OperationQueryDto query, long factoryId)
    {
        var q = _db.Operations.AsNoTracking().Where(o => o.FactoryId == factoryId);
        var fieldKey = string.IsNullOrWhiteSpace(query.FilterField) ? "" : query.FilterField.Trim();
        var filterIsStatus = fieldKey.Equals("status", StringComparison.OrdinalIgnoreCase);
        var filterIsAudit = fieldKey.Equals("auditStatus", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(query.FilterField))
            q = ApplyQuickFilter(q, query);
        else if (!string.IsNullOrWhiteSpace(query.Keyword))
            q = q.Where(o => o.Code.Contains(query.Keyword) || o.Name.Contains(query.Keyword));

        if (!filterIsStatus)
        {
            if (query.Status.HasValue) q = q.Where(o => o.Status == query.Status.Value);
            else if (!query.IncludeDisabled) q = q.Where(o => o.Status == 1);
        }
        if (!filterIsAudit && !query.IncludeUnaudited)
            q = q.Where(o => o.AuditStatus == 1);

        var total = await q.CountAsync();
        var ops = await q.OrderBy(o => o.Code).ThenBy(o => o.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync();

        var opIds = ops.Select(o => o.Id).ToList();
        var od = await _db.OperationDepartments.AsNoTracking().Where(x => opIds.Contains(x.OperationId)).ToListAsync();
        var of = await _db.OperationDefects.AsNoTracking().Where(x => opIds.Contains(x.OperationId)).ToListAsync();
        var depts = await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name);
        var defects = await _db.DefectItems.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name);

        var list = ops.Select(o => new OperationListDto
        {
            Id = o.Id,
            Code = o.Code,
            Name = o.Name,
            Status = o.Status,
            AuditStatus = o.AuditStatus,
            DeptNames = string.Join("、", od.Where(x => x.OperationId == o.Id).Select(x => depts.GetValueOrDefault(x.DepartmentId, ""))),
            DefectNames = string.Join("、", of.Where(x => x.OperationId == o.Id).Select(x => defects.GetValueOrDefault(x.DefectId, "")))
        }).ToList();

        return ApiResult<PageResult<OperationListDto>>.Ok(new PageResult<OperationListDto> { List = list, Total = total });
    }

    private static IQueryable<BaseOperation> ApplyQuickFilter(IQueryable<BaseOperation> q, OperationQueryDto query)
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

    public async Task<ApiResult<OperationDetailDto>> GetAsync(long id, long factoryId)
    {
        var op = await _db.Operations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(GetAsync), "工序不存在");
        var deptIds = await _db.OperationDepartments.AsNoTracking()
            .Where(x => x.OperationId == id).Select(x => x.DepartmentId).ToListAsync();
        var defectIds = await _db.OperationDefects.AsNoTracking()
            .Where(x => x.OperationId == id).Select(x => x.DefectId).ToListAsync();
        var userIds = new[] { op.AuditedBy, op.ForbidBy, op.UnforbidBy }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var names = userIds.Count == 0
            ? new Dictionary<long, string>()
            : await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name);
        return ApiResult<OperationDetailDto>.Ok(new OperationDetailDto
        {
            Id = op.Id,
            Code = op.Code,
            Name = op.Name,
            DeptIds = deptIds,
            DefectIds = defectIds,
            Status = op.Status,
            AuditStatus = op.AuditStatus,
            AuditedBy = op.AuditedBy,
            AuditedByName = op.AuditedBy.HasValue ? names.GetValueOrDefault(op.AuditedBy.Value) : null,
            AuditedAt = op.AuditedAt,
            ForbidBy = op.ForbidBy,
            ForbidByName = op.ForbidBy.HasValue ? names.GetValueOrDefault(op.ForbidBy.Value) : null,
            ForbidAt = op.ForbidAt,
            UnforbidBy = op.UnforbidBy,
            UnforbidByName = op.UnforbidBy.HasValue ? names.GetValueOrDefault(op.UnforbidBy.Value) : null,
            UnforbidAt = op.UnforbidAt
        });
    }

    public async Task<ApiResult<object?>> CreateAsync(OperationCreateDto dto, long factoryId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        if (dto.CopyFromId.HasValue && !await _db.Operations.AnyAsync(o => o.Id == dto.CopyFromId && o.FactoryId == factoryId))
            throw ThrowHelper.Biz(nameof(CreateAsync), "复制工序时指定的来源工序不存在或不属于本厂。请到「工序」确认来源工序编号正确且归属本厂。");
        await ValidateLinksAsync(dto, factoryId);
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Name))
            throw ThrowHelper.Biz(nameof(CreateAsync), "工序编号和名称都必须填写。请填写完整的工序编号和名称后再保存。");

        var op = new BaseOperation
        {
            FactoryId = factoryId,
            Code = dto.Code.Trim(),
            Name = dto.Name.Trim(),
            Status = 1,
            AuditStatus = 0,
            CreatedAt = DateTime.Now
        };
        _db.Operations.Add(op);
        await _db.SaveChangesAsync();
        await SaveLinks(op.Id, dto);
        await tx.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }

    private async Task ValidateLinksAsync(OperationCreateDto dto, long factoryId)
    {
        var depts = (dto.DeptIds ?? new()).Distinct().ToList();
        var defects = (dto.DefectIds ?? new()).Distinct().ToList();
        if (await _db.Departments.CountAsync(d => d.FactoryId == factoryId && depts.Contains(d.Id)) != depts.Count)
            throw ThrowHelper.Biz(nameof(ValidateLinksAsync), "工序关联的报工权限部门不存在或不属于本厂。请到「部门」确认部门已创建且归属本厂，再回工序编辑页重新选择权限部门。注意：报工权限按部门控制，不是按人。");
        if (await _db.DefectItems.CountAsync(d => d.FactoryId == factoryId && defects.Contains(d.Id)) != defects.Count)
            throw ThrowHelper.Biz(nameof(ValidateLinksAsync), "工序关联的不良品项不存在或不属于本厂。请到「不良品项管理」确认该项已创建且归属本厂，再回工序编辑页重新选择。");
        dto.DeptIds = depts;
        dto.DefectIds = defects;
    }

    public async Task<ApiResult<object?>> UpdateAsync(long id, OperationCreateDto dto, long factoryId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        await ValidateLinksAsync(dto, factoryId);
        var op = await _db.Operations.FirstOrDefaultAsync(o => o.Id == id && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "工序不存在");
        if (op.AuditStatus == 1)
            throw ThrowHelper.Biz(nameof(UpdateAsync), "已审核的工序不能直接改。请先点「反审核」，改完再审核。");
        op.Code = dto.Code.Trim();
        op.Name = dto.Name.Trim();
        _db.OperationDepartments.RemoveRange(_db.OperationDepartments.Where(x => x.OperationId == id));
        _db.OperationDefects.RemoveRange(_db.OperationDefects.Where(x => x.OperationId == id));
        await _db.SaveChangesAsync();
        await SaveLinks(id, dto);
        await tx.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }

    private async Task SaveLinks(long opId, OperationCreateDto dto)
    {
        foreach (var d in dto.DeptIds.Distinct())
            _db.OperationDepartments.Add(new BaseOperationDepartment { OperationId = opId, DepartmentId = d });
        foreach (var d in dto.DefectIds.Distinct())
            _db.OperationDefects.Add(new BaseOperationDefect { OperationId = opId, DefectId = d });
        await _db.SaveChangesAsync();
    }

    public async Task<ApiResult<object?>> DeleteAsync(long id)
    {
        var msg = await DeleteGuard.CheckAsync(_db, "Operation", id);
        if (msg != null) throw ThrowHelper.Biz(nameof(DeleteAsync), msg);

        var op = await _db.Operations.FindAsync(id)
            ?? throw ThrowHelper.Biz(nameof(DeleteAsync), "工序不存在");
        if (op.AuditStatus == 1)
            throw ThrowHelper.Biz(nameof(DeleteAsync), "已审核的工序不能直接删。请先反审核，确认无引用后再删。");
        _db.OperationDepartments.RemoveRange(_db.OperationDepartments.Where(x => x.OperationId == id));
        _db.OperationDefects.RemoveRange(_db.OperationDefects.Where(x => x.OperationId == id));
        _db.Operations.Remove(op);
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> ForbidAsync(long id, long factoryId, long userId)
    {
        var op = await _db.Operations.FirstOrDefaultAsync(o => o.Id == id && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(ForbidAsync), "工序不存在");
        if (op.Status == 0)
            throw ThrowHelper.Biz(nameof(ForbidAsync), "该工序已经是禁用状态，无需再禁用。");
        op.Status = 0;
        op.ForbidBy = userId;
        op.ForbidAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return new ApiResult<object?> { Code = 0, Msg = "已禁用；新工艺路线不能再选这道工序。" };
    }

    public async Task<ApiResult<object?>> UnforbidAsync(long id, long factoryId, long userId)
    {
        var op = await _db.Operations.FirstOrDefaultAsync(o => o.Id == id && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UnforbidAsync), "工序不存在");
        if (op.Status == 1)
            throw ThrowHelper.Biz(nameof(UnforbidAsync), "该工序已经是启用状态，无需再反禁用。");
        op.Status = 1;
        op.UnforbidBy = userId;
        op.UnforbidAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return new ApiResult<object?> { Code = 0, Msg = "已反禁用；若已审核则可再次加入工艺路线。" };
    }

    public async Task<ApiResult<object?>> AuditAsync(long id, long factoryId, long userId)
    {
        var op = await _db.Operations.FirstOrDefaultAsync(o => o.Id == id && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(AuditAsync), "工序不存在");
        if (op.AuditStatus == 1)
            throw ThrowHelper.Biz(nameof(AuditAsync), "该工序已经审核过了，无需再审。");
        op.AuditStatus = 1;
        op.AuditedBy = userId;
        op.AuditedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return new ApiResult<object?> { Code = 0, Msg = "已审核；启用状态下可加入工艺路线。" };
    }

    public async Task<ApiResult<object?>> UnauditAsync(long id, long factoryId, long userId)
    {
        var op = await _db.Operations.FirstOrDefaultAsync(o => o.Id == id && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UnauditAsync), "工序不存在");
        if (op.AuditStatus != 1)
            throw ThrowHelper.Biz(nameof(UnauditAsync), "当前不需要反审核。");
        op.AuditStatus = 0;
        _ = userId;
        await _db.SaveChangesAsync();
        return new ApiResult<object?> { Code = 0, Msg = "已反审核；请改完后再审核。" };
    }
}
