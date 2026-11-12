using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IDocumentPermissionService
{
    Task<ApiResult<DocumentPermissionDto>> GetAsync(long factoryId);
    Task<ApiResult<object?>> SaveAsync(DocumentPermissionDto dto, long factoryId, byte role);
    Task EnsureCanEditWorkOrderAsync(long factoryId, byte role, long userId, long? createdBy);
    Task EnsureCanEditPriceListAsync(long factoryId, byte role, long userId, long? createdBy);
}

public class DocumentPermissionDto
{
    /// <summary>仅创建人可改工单；默认 false</summary>
    public bool OnlyCreatorEditWorkOrder { get; set; }
    /// <summary>仅创建人可改价目表；默认 false</summary>
    public bool OnlyCreatorEditPriceList { get; set; }
}

public class DocumentPermissionService : IDocumentPermissionService
{
    private readonly AppDbContext _db;
    public DocumentPermissionService(AppDbContext db) { _db = db; }

    public async Task<ApiResult<DocumentPermissionDto>> GetAsync(long factoryId)
    {
        var row = await _db.DocumentPermissionSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.FactoryId == factoryId);
        return ApiResult<DocumentPermissionDto>.Ok(ToDto(row));
    }

    public async Task<ApiResult<object?>> SaveAsync(DocumentPermissionDto dto, long factoryId, byte role)
    {
        if (role != 1)
            throw ThrowHelper.BizUser("改单据权限开关需要管理员。请用管理员账号到「系统配置 → 单据权限」操作。");
        if (dto == null)
            throw ThrowHelper.Biz(nameof(SaveAsync), "单据权限保存失败：请求体为空。");

        var row = await _db.DocumentPermissionSettings.FirstOrDefaultAsync(x => x.FactoryId == factoryId);
        if (row == null)
        {
            row = new SysDocumentPermissionSetting { FactoryId = factoryId };
            _db.DocumentPermissionSettings.Add(row);
        }

        row.OnlyCreatorEditWorkOrder = dto.OnlyCreatorEditWorkOrder;
        row.OnlyCreatorEditPriceList = dto.OnlyCreatorEditPriceList;
        row.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task EnsureCanEditWorkOrderAsync(long factoryId, byte role, long userId, long? createdBy)
    {
        if (role == 1) return;
        var on = await _db.DocumentPermissionSettings.AsNoTracking()
            .Where(x => x.FactoryId == factoryId)
            .Select(x => (bool?)x.OnlyCreatorEditWorkOrder)
            .FirstOrDefaultAsync() == true;
        if (!on) return;
        if (createdBy is long cb && cb == userId) return;
        throw ThrowHelper.BizUser("这张工单不是你创建的，不能改。请联系创建人或管理员处理。");
    }

    public async Task EnsureCanEditPriceListAsync(long factoryId, byte role, long userId, long? createdBy)
    {
        if (role == 1) return;
        var on = await _db.DocumentPermissionSettings.AsNoTracking()
            .Where(x => x.FactoryId == factoryId)
            .Select(x => (bool?)x.OnlyCreatorEditPriceList)
            .FirstOrDefaultAsync() == true;
        if (!on) return;
        if (createdBy is long cb && cb == userId) return;
        throw ThrowHelper.BizUser("这张价目表不是你创建的，不能改。请联系创建人或管理员处理。");
    }

    private static DocumentPermissionDto ToDto(SysDocumentPermissionSetting? row) => new()
    {
        OnlyCreatorEditWorkOrder = row?.OnlyCreatorEditWorkOrder == true,
        OnlyCreatorEditPriceList = row?.OnlyCreatorEditPriceList == true
    };
}
