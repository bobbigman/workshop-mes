using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IUnitService
{
    Task<ApiResult<List<UnitDto>>> QueryAsync(long factoryId);
    Task<ApiResult<object?>> CreateAsync(UnitDto dto, long factoryId);
    Task<ApiResult<object?>> UpdateAsync(long id, UnitDto dto, long factoryId);
    Task<ApiResult<object?>> DeleteAsync(long id);
}

public class UnitDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
}

public class UnitService : IUnitService
{
    private readonly AppDbContext _db;
    public UnitService(AppDbContext db) { _db = db; }

    public async Task<ApiResult<List<UnitDto>>> QueryAsync(long factoryId)
    {
        var list = await _db.Units.AsNoTracking()
            .Where(u => u.FactoryId == factoryId)
            .OrderBy(u => u.Id)
            .Select(u => new UnitDto { Id = u.Id, Name = u.Name })
            .ToListAsync();
        return ApiResult<List<UnitDto>>.Ok(list);
    }

    public async Task<ApiResult<object?>> CreateAsync(UnitDto dto, long factoryId)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw ThrowHelper.Biz(nameof(CreateAsync), "单位名称不能为空");
        _db.Units.Add(new BaseUnit { FactoryId = factoryId, Name = dto.Name.Trim(), CreatedAt = DateTime.Now });
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> UpdateAsync(long id, UnitDto dto, long factoryId)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw ThrowHelper.Biz(nameof(UpdateAsync), "单位名称不能为空");
        var entity = await _db.Units.FirstOrDefaultAsync(u => u.Id == id && u.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "单位不存在");
        entity.Name = dto.Name.Trim();
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> DeleteAsync(long id)
    {
        var msg = await DeleteGuard.CheckAsync(_db, "Unit", id);
        if (msg != null) throw ThrowHelper.Biz(nameof(DeleteAsync), msg);
        var entity = await _db.Units.FindAsync(id)
            ?? throw ThrowHelper.Biz(nameof(DeleteAsync), "单位不存在");
        _db.Units.Remove(entity);
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }
}
