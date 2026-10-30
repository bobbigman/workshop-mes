using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IWorkerViewService
{
    Task<byte> GetModeAsync(long factoryId);
    Task<ApiResult<WorkerViewDto>> GetAsync(long factoryId);
    Task<ApiResult<object?>> SaveAsync(WorkerViewDto dto, long factoryId, byte role);
}

public class WorkerViewDto
{
    /// <summary>1=全车间可见(默认) 2=只看我的任务</summary>
    public byte WorkerViewMode { get; set; } = 1;
}

public class WorkerViewService : IWorkerViewService
{
    public const byte ModeAll = 1;
    public const byte ModeMyTasks = 2;

    private readonly AppDbContext _db;
    public WorkerViewService(AppDbContext db) { _db = db; }

    public async Task<byte> GetModeAsync(long factoryId)
    {
        var row = await _db.WorkerViewSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.FactoryId == factoryId);
        return row == null ? ModeAll : Normalize(row.WorkerViewMode);
    }

    public async Task<ApiResult<WorkerViewDto>> GetAsync(long factoryId)
    {
        var mode = await GetModeAsync(factoryId);
        return ApiResult<WorkerViewDto>.Ok(new WorkerViewDto { WorkerViewMode = mode });
    }

    public async Task<ApiResult<object?>> SaveAsync(WorkerViewDto dto, long factoryId, byte role)
    {
        if (role != 1)
            throw ThrowHelper.BizUser("修改工人手机端视角需要管理员权限，当前账号权限不足，请切换到管理员账号操作。");
        if (dto == null)
            throw ThrowHelper.Biz(nameof(SaveAsync), "当前用户的报工视角未设置，请到「用户」检查该用户的默认视角设置。");

        var mode = Normalize(dto.WorkerViewMode);
        if (mode != ModeAll && mode != ModeMyTasks)
            throw ThrowHelper.BizUser("视角只能选“全车间可见”或“只看我的任务”两种，请检查选择的选项是否正确。");

        var row = await _db.WorkerViewSettings.FirstOrDefaultAsync(x => x.FactoryId == factoryId);
        if (row == null)
        {
            row = new SysWorkerViewSetting { FactoryId = factoryId };
            _db.WorkerViewSettings.Add(row);
        }

        row.WorkerViewMode = mode;
        row.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    private static byte Normalize(byte mode) =>
        mode == ModeMyTasks ? ModeMyTasks : ModeAll;
}
