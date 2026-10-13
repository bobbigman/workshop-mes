using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IReportQtySettingService
{
    Task<ReportQtySettingDto> GetOrDefaultAsync(long factoryId);
    Task<ApiResult<ReportQtySettingDto>> GetAsync(long factoryId);
    Task<ApiResult<object?>> SaveAsync(ReportQtySettingDto dto, long factoryId, byte role);
}

public class ReportQtySettingDto
{
    /// <summary>允许超报比例%，0～999，默认 0。</summary>
    public int OverrunRatio { get; set; }
    /// <summary>true=上道已复核不良计入下道可报。</summary>
    public bool CountDefect { get; set; }
}

public class ReportQtySettingService : IReportQtySettingService
{
    private readonly AppDbContext _db;
    public ReportQtySettingService(AppDbContext db) { _db = db; }

    /// <summary>允许件数。比例为 0 时等于计划数；否则 ceil(计划 × (1+比例/100))。</summary>
    public static int AllowedQty(int planQty, int overrunRatio)
    {
        if (planQty <= 0 || overrunRatio <= 0) return planQty < 0 ? 0 : planQty;
        var raw = (long)planQty * (100 + overrunRatio);
        var allowed = (raw + 99L) / 100L;
        return allowed > int.MaxValue ? int.MaxValue : (int)allowed;
    }

    public async Task<ReportQtySettingDto> GetOrDefaultAsync(long factoryId)
    {
        var row = await _db.ReportQtySettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.FactoryId == factoryId);
        return row == null ? new ReportQtySettingDto() : ToDto(row);
    }

    public async Task<ApiResult<ReportQtySettingDto>> GetAsync(long factoryId) =>
        ApiResult<ReportQtySettingDto>.Ok(await GetOrDefaultAsync(factoryId));

    public async Task<ApiResult<object?>> SaveAsync(ReportQtySettingDto dto, long factoryId, byte role)
    {
        if (role != 1)
            throw ThrowHelper.BizUser("仅管理员可修改报工数量设置");
        if (dto == null)
            throw ThrowHelper.Biz(nameof(SaveAsync), "报工数量设置为空");
        if (dto.OverrunRatio < 0 || dto.OverrunRatio > 999)
            throw ThrowHelper.BizUser("允许超报比例须为 0～999 的整数");

        var row = await _db.ReportQtySettings.FirstOrDefaultAsync(x => x.FactoryId == factoryId);
        if (row == null)
        {
            row = new SysReportQtySetting { FactoryId = factoryId };
            _db.ReportQtySettings.Add(row);
        }

        row.OverrunRatio = dto.OverrunRatio;
        row.CountDefect = dto.CountDefect;
        row.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    private static ReportQtySettingDto ToDto(SysReportQtySetting row) => new()
    {
        OverrunRatio = row.OverrunRatio,
        CountDefect = row.CountDefect
    };
}
