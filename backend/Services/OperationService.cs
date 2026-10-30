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
}

public class OperationQueryDto
{
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
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
}

public class OperationDetailDto
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public List<long> DeptIds { get; set; } = new();
    public List<long> DefectIds { get; set; } = new();
}

public class OperationService : IOperationService
{
    private readonly AppDbContext _db;
    public OperationService(AppDbContext db) { _db = db; }

    public async Task<ApiResult<PageResult<OperationListDto>>> QueryAsync(OperationQueryDto query, long factoryId)
    {
        var q = _db.Operations.AsNoTracking().Where(o => o.FactoryId == factoryId);
        if (!string.IsNullOrWhiteSpace(query.Keyword))
            q = q.Where(o => o.Code.Contains(query.Keyword) || o.Name.Contains(query.Keyword));

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
            DeptNames = string.Join("、", od.Where(x => x.OperationId == o.Id).Select(x => depts.GetValueOrDefault(x.DepartmentId, ""))),
            DefectNames = string.Join("、", of.Where(x => x.OperationId == o.Id).Select(x => defects.GetValueOrDefault(x.DefectId, "")))
        }).ToList();

        return ApiResult<PageResult<OperationListDto>>.Ok(new PageResult<OperationListDto> { List = list, Total = total });
    }

    public async Task<ApiResult<OperationDetailDto>> GetAsync(long id, long factoryId)
    {
        var op = await _db.Operations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(GetAsync), "工序不存在");
        var deptIds = await _db.OperationDepartments.AsNoTracking()
            .Where(x => x.OperationId == id).Select(x => x.DepartmentId).ToListAsync();
        var defectIds = await _db.OperationDefects.AsNoTracking()
            .Where(x => x.OperationId == id).Select(x => x.DefectId).ToListAsync();
        return ApiResult<OperationDetailDto>.Ok(new OperationDetailDto
        {
            Id = op.Id,
            Code = op.Code,
            Name = op.Name,
            DeptIds = deptIds,
            DefectIds = defectIds
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

        _db.OperationDepartments.RemoveRange(_db.OperationDepartments.Where(x => x.OperationId == id));
        _db.OperationDefects.RemoveRange(_db.OperationDefects.Where(x => x.OperationId == id));
        var op = await _db.Operations.FindAsync(id)
            ?? throw ThrowHelper.Biz(nameof(DeleteAsync), "工序不存在");
        _db.Operations.Remove(op);
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }
}
