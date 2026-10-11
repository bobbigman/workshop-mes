using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IReportUpstreamSettingService
{
    Task<ReportUpstreamSettingDto> GetOrDefaultAsync(long factoryId);
    Task<ApiResult<ReportUpstreamSettingDto>> GetAsync(long factoryId);
    Task<ApiResult<object?>> SaveAsync(ReportUpstreamSettingDto dto, long factoryId, byte role);
}

public class ReportUpstreamSettingDto
{
    /// <summary>soft | hard；默认 soft</summary>
    public string Mode { get; set; } = "soft";
    /// <summary>硬拦截工序 ID；mode=hard 且空=全部硬拦</summary>
    public List<long> HardOperationIds { get; set; } = new();
}

public class ReportUpstreamSettingService : IReportUpstreamSettingService
{
    public const string ModeSoft = "soft";
    public const string ModeHard = "hard";

    private readonly AppDbContext _db;
    public ReportUpstreamSettingService(AppDbContext db) { _db = db; }

    public async Task<ReportUpstreamSettingDto> GetOrDefaultAsync(long factoryId)
    {
        var row = await _db.ReportUpstreamSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.FactoryId == factoryId);
        return row == null ? DefaultDto() : ToDto(row);
    }

    public async Task<ApiResult<ReportUpstreamSettingDto>> GetAsync(long factoryId) =>
        ApiResult<ReportUpstreamSettingDto>.Ok(await GetOrDefaultAsync(factoryId));

    public async Task<ApiResult<object?>> SaveAsync(ReportUpstreamSettingDto dto, long factoryId, byte role)
    {
        if (role != 1)
            throw ThrowHelper.BizUser("仅管理员可修改报工上级校验");
        if (dto == null)
            throw ThrowHelper.Biz(nameof(SaveAsync), "校验设置为空");

        var mode = NormalizeMode(dto.Mode);
        var ids = (dto.HardOperationIds ?? new List<long>())
            .Where(x => x > 0).Distinct().OrderBy(x => x).ToList();
        if (ids.Count > 200)
            throw ThrowHelper.BizUser("硬拦截工序最多 200 个");

        var json = ids.Count == 0 ? null : JsonSerializer.Serialize(ids);

        var row = await _db.ReportUpstreamSettings.FirstOrDefaultAsync(x => x.FactoryId == factoryId);
        if (row == null)
        {
            row = new SysReportUpstreamSetting { FactoryId = factoryId };
            _db.ReportUpstreamSettings.Add(row);
        }

        row.Mode = mode;
        row.HardOperationIds = json;
        row.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    public static string NormalizeMode(string? mode) =>
        string.Equals(mode, ModeHard, StringComparison.OrdinalIgnoreCase) ? ModeHard : ModeSoft;

    public static HashSet<long> ParseHardIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new HashSet<long>();
        try
        {
            var list = JsonSerializer.Deserialize<List<long>>(json);
            return list == null ? new HashSet<long>() : list.Where(x => x > 0).ToHashSet();
        }
        catch (JsonException)
        {
            return new HashSet<long>();
        }
    }

    /// <summary>mode=hard 且（清单空或工序在清单内）→ 硬拦；否则 soft。</summary>
    public static bool IsHardForOp(ReportUpstreamSettingDto cfg, long operationId)
    {
        if (!string.Equals(cfg.Mode, ModeHard, StringComparison.OrdinalIgnoreCase))
            return false;
        if (cfg.HardOperationIds == null || cfg.HardOperationIds.Count == 0)
            return true;
        return cfg.HardOperationIds.Contains(operationId);
    }

    private static ReportUpstreamSettingDto DefaultDto() => new() { Mode = ModeSoft, HardOperationIds = new() };

    private static ReportUpstreamSettingDto ToDto(SysReportUpstreamSetting row) => new()
    {
        Mode = NormalizeMode(row.Mode),
        HardOperationIds = ParseHardIds(row.HardOperationIds).OrderBy(x => x).ToList()
    };
}
