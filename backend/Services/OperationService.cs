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
        var ops = await q.OrderByDescending(o => o.Id)
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
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Name))
            throw ThrowHelper.Biz(nameof(CreateAsync), "工序编号和名称不能为空");

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
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> UpdateAsync(long id, OperationCreateDto dto, long factoryId)
    {
        var op = await _db.Operations.FirstOrDefaultAsync(o => o.Id == id && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "工序不存在");
        op.Code = dto.Code.Trim();
        op.Name = dto.Name.Trim();
        _db.OperationDepartments.RemoveRange(_db.OperationDepartments.Where(x => x.OperationId == id));
        _db.OperationDefects.RemoveRange(_db.OperationDefects.Where(x => x.OperationId == id));
        await _db.SaveChangesAsync();
        await SaveLinks(id, dto);
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
