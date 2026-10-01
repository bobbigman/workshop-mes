using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IDefectItemService
{
    Task<ApiResult<List<DefectItemDto>>> QueryAsync(long factoryId);
    Task<ApiResult<object?>> CreateAsync(DefectItemDto dto, long factoryId);
    Task<ApiResult<object?>> UpdateAsync(long id, DefectItemDto dto, long factoryId);
    Task<ApiResult<object?>> DeleteAsync(long id);
}

public class DefectItemDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
}

public class DefectItemService : IDefectItemService
{
    private readonly AppDbContext _db;
    public DefectItemService(AppDbContext db) { _db = db; }

    public async Task<ApiResult<List<DefectItemDto>>> QueryAsync(long factoryId)
    {
        var list = await _db.DefectItems.AsNoTracking()
            .Where(d => d.FactoryId == factoryId)
            .OrderBy(d => d.Id)
            .Select(d => new DefectItemDto { Id = d.Id, Name = d.Name })
            .ToListAsync();
        return ApiResult<List<DefectItemDto>>.Ok(list);
    }

    public async Task<ApiResult<object?>> CreateAsync(DefectItemDto dto, long factoryId)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw ThrowHelper.Biz(nameof(CreateAsync), "不良品项名称不能为空");
        _db.DefectItems.Add(new BaseDefectItem { FactoryId = factoryId, Name = dto.Name.Trim(), CreatedAt = DateTime.Now });
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> UpdateAsync(long id, DefectItemDto dto, long factoryId)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw ThrowHelper.Biz(nameof(UpdateAsync), "不良品项名称不能为空");
        var entity = await _db.DefectItems.FirstOrDefaultAsync(d => d.Id == id && d.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(UpdateAsync), "不良品项不存在");
        entity.Name = dto.Name.Trim();
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<object?>> DeleteAsync(long id)
    {
        var msg = await DeleteGuard.CheckAsync(_db, "DefectItem", id);
        if (msg != null) throw ThrowHelper.Biz(nameof(DeleteAsync), msg);
        var entity = await _db.DefectItems.FindAsync(id)
            ?? throw ThrowHelper.Biz(nameof(DeleteAsync), "不良品项不存在");
        _db.DefectItems.Remove(entity);
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }
}
