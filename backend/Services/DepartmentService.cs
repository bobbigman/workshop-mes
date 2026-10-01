using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IDepartmentService
{
    Task<ApiResult<PageResult<DepartmentListDto>>> QueryAsync(DepartmentQueryDto query, long factoryId);
    Task<ApiResult<DepartmentDetailDto>> GetAsync(long id, long factoryId);
    Task<ApiResult<object?>> CreateAsync(DepartmentCreateDto dto, long factoryId);
    Task<ApiResult<object?>> UpdateAsync(long id, DepartmentCreateDto dto, long factoryId);
    Task<ApiResult<object?>> DeleteAsync(long id);
}

public class DepartmentQueryDto
{
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class DepartmentCreateDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public List<long> MemberIds { get; set; } = new();
}

public class DepartmentListDto
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Members { get; set; } = "";
}

public class DepartmentDetailDto
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public List<long> MemberIds { get; set; } = new();
}

public class DepartmentService : IDepartmentService
{
    private readonly AppDbContext _db;
    public DepartmentService(AppDbContext db) { _db = db; }

    public async Task<ApiResult<PageResult<DepartmentListDto>>> QueryAsync(DepartmentQueryDto query, long factoryId)
    {
        var q = _db.Departments.AsNoTracking().Where(d => d.FactoryId == factoryId);
        if (!string.IsNullOrWhiteSpace(query.Keyword))
            q = q.Where(d => d.Code.Contains(query.Keyword) || d.Name.Contains(query.Keyword));

        var total = await q.CountAsync();
        var depts = await q.OrderByDescending(d => d.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync();

        var deptIds = depts.Select(d => d.Id).ToList();
        var links = await _db.DepartmentUsers.AsNoTracking()
            .Where(x => deptIds.Contains(x.DepartmentId)).ToListAsync();
        var userIds = links.Select(x => x.UserId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name);

        var list = depts.Select(d => new DepartmentListDto
        {
            Id = d.Id,
            Code = d.Code,
            Name = d.Name,
            Members = string.Join("、", links.Where(l => l.DepartmentId == d.Id)
                .Select(l => users.GetValueOrDefault(l.UserId, "")))
        }).ToList();

        return ApiResult<PageResult<DepartmentListDto>>.Ok(new PageResult<DepartmentListDto> { List = list, Total = total });
    }

    public async Task<ApiResult<DepartmentDetailDto>> GetAsync(long id, long factoryId)
    {
        var d = await _db.Departments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(GetAsync), "部门不存在");
        var memberIds = await _db.DepartmentUsers.AsNoTracking()
            .Where(x => x.DepartmentId == id).Select(x => x.UserId).ToListAsync();
        return ApiResult<DepartmentDetailDto>.Ok(new DepartmentDetailDto
        {
            Id = d.Id,
            Code = d.Code,
            Name = d.Name,
            MemberIds = memberIds
        });
    }

    public async Task<ApiResult<object?>> CreateAsync(DepartmentCreateDto dto, long factoryId)
    {
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Name))
            throw ThrowHelper.Biz(nameof(CreateAsync), "部门编码和名称不能为空");

        var dept = new SysDepartment
        {
            FactoryId = factoryId,
            Code = dto.Code.Trim(),
            Name = dto.Name.Trim(),
            CreatedAt = DateTime.Now
        };
        _db.Departments.Add(dept);
        await _db.SaveChangesAsync();

        foreach (var uid in dto.MemberIds.Distinct())
            _db.DepartmentUsers.Add(new SysDepartmentUser { DepartmentId = dept.Id, UserId = uid });
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> UpdateAsync(long id, DepartmentCreateDto dto, long factoryId)
    {
        var dept = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id && d.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "部门不存在");

        dept.Code = dto.Code.Trim();
        dept.Name = dto.Name.Trim();

        var old = _db.DepartmentUsers.Where(x => x.DepartmentId == id);
        _db.DepartmentUsers.RemoveRange(old);
        foreach (var uid in dto.MemberIds.Distinct())
            _db.DepartmentUsers.Add(new SysDepartmentUser { DepartmentId = id, UserId = uid });

        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> DeleteAsync(long id)
    {
        var msg = await DeleteGuard.CheckAsync(_db, "Department", id);
        if (msg != null) throw ThrowHelper.Biz(nameof(DeleteAsync), msg);

        var dept = await _db.Departments.FindAsync(id)
            ?? throw ThrowHelper.Biz(nameof(DeleteAsync), "部门不存在");
        var links = _db.DepartmentUsers.Where(x => x.DepartmentId == id);
        _db.DepartmentUsers.RemoveRange(links);
        _db.Departments.Remove(dept);
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }
}
