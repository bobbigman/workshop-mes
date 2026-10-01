using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IPrintSettingService
{
    Task<ApiResult<PrintSettingDto>> GetAsync(long factoryId);
    Task<ApiResult<object?>> SaveAsync(PrintSettingDto dto, long factoryId);
}

public class PrintSettingDto
{
    public string LabelSize { get; set; } = "Label80x60";
    public string QrMode { get; set; } = "OrderNo";
    public string? BaseUrl { get; set; }
    public bool ShowProductCode { get; set; } = true;
    public bool ShowProductName { get; set; } = true;
    public bool ShowQty { get; set; } = true;
    public bool ShowOps { get; set; } = true;
    /// <summary>要打印的工单自定义字段 ID</summary>
    public List<long> ShowCustomFieldIds { get; set; } = new();
}

public class PrintSettingService : IPrintSettingService
{
    private static readonly HashSet<string> LabelSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Label30x40", "Label80x60"
    };
    private static readonly HashSet<string> QrModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "OrderNo", "ReportUrl", "ReportUrlOp"
    };

    private readonly AppDbContext _db;
    public PrintSettingService(AppDbContext db) { _db = db; }

    public async Task<ApiResult<PrintSettingDto>> GetAsync(long factoryId)
    {
        var row = await _db.PrintSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.FactoryId == factoryId);
        return ApiResult<PrintSettingDto>.Ok(row == null ? DefaultDto() : ToDto(row));
    }

    public async Task<ApiResult<object?>> SaveAsync(PrintSettingDto dto, long factoryId)
    {
        if (dto == null)
            throw ThrowHelper.Biz(nameof(SaveAsync), "打印设置为空");

        var labelSize = (dto.LabelSize ?? "").Trim();
        var qrMode = (dto.QrMode ?? "").Trim();
        if (!LabelSizes.Contains(labelSize))
            throw ThrowHelper.Biz(nameof(SaveAsync), "母版尺寸无效，仅支持 Label30x40 / Label80x60");
        if (!QrModes.Contains(qrMode))
            throw ThrowHelper.Biz(nameof(SaveAsync), "二维码类型无效，仅支持 OrderNo / ReportUrl / ReportUrlOp");

        string? baseUrl = string.IsNullOrWhiteSpace(dto.BaseUrl) ? null : dto.BaseUrl.Trim().TrimEnd('/');
        var needsBase = string.Equals(qrMode, "ReportUrl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(qrMode, "ReportUrlOp", StringComparison.OrdinalIgnoreCase);
        if (needsBase && !string.IsNullOrEmpty(baseUrl))
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw ThrowHelper.Biz(nameof(SaveAsync), "报工页基址须为 http/https 绝对地址");
        }

        var customIds = (dto.ShowCustomFieldIds ?? new List<long>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        if (customIds.Count > 0)
        {
            var validCount = await _db.CustomFields.AsNoTracking()
                .CountAsync(f => f.FactoryId == factoryId
                                 && f.Target == "work_order"
                                 && customIds.Contains(f.Id));
            if (validCount != customIds.Count)
                throw ThrowHelper.Biz(nameof(SaveAsync), "自定义字段无效或不属于本工厂工单字段");
        }

        var row = await _db.PrintSettings.FirstOrDefaultAsync(x => x.FactoryId == factoryId);
        if (row == null)
        {
            row = new SysPrintSetting { FactoryId = factoryId };
            _db.PrintSettings.Add(row);
        }

        row.LabelSize = LabelSizes.First(x => x.Equals(labelSize, StringComparison.OrdinalIgnoreCase));
        row.QrMode = QrModes.First(x => x.Equals(qrMode, StringComparison.OrdinalIgnoreCase));
        row.BaseUrl = baseUrl;
        row.ShowProductCode = dto.ShowProductCode;
        row.ShowProductName = dto.ShowProductName;
        row.ShowQty = dto.ShowQty;
        row.ShowOps = dto.ShowOps;
        row.ShowCustomFieldIds = customIds.Count == 0 ? null : JsonSerializer.Serialize(customIds);
        row.UpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    private static PrintSettingDto DefaultDto() => new();

    private static PrintSettingDto ToDto(SysPrintSetting row) => new()
    {
        LabelSize = row.LabelSize,
        QrMode = row.QrMode,
        BaseUrl = row.BaseUrl,
        ShowProductCode = row.ShowProductCode,
        ShowProductName = row.ShowProductName,
        ShowQty = row.ShowQty,
        ShowOps = row.ShowOps,
        ShowCustomFieldIds = ParseCustomFieldIds(row.ShowCustomFieldIds)
    };

    private static List<long> ParseCustomFieldIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<long>();
        try
        {
            var list = JsonSerializer.Deserialize<List<long>>(raw);
            return list?.Where(id => id > 0).Distinct().ToList() ?? new List<long>();
        }
        catch (Exception ex)
        {
            throw ThrowHelper.Biz(nameof(ParseCustomFieldIds),
                $"解析打印自定义字段配置失败，原文={raw}，原因={ex.Message}");
        }
    }
}
