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
            throw ThrowHelper.Biz(nameof(SaveAsync), "提交的打印设置为空。请至少设置一项打印内容后再保存。");

        var labelSize = (dto.LabelSize ?? "").Trim();
        var qrMode = (dto.QrMode ?? "").Trim();
        if (!LabelSizes.Contains(labelSize))
            throw ThrowHelper.Biz(nameof(SaveAsync), "打印母版尺寸只支持 Label30x40 和 Label80x60 两种规格。请在打印设置中选择这两种尺寸之一。");
        if (!QrModes.Contains(qrMode))
            throw ThrowHelper.Biz(nameof(SaveAsync), "二维码类型只支持 OrderNo（工单号）、ReportUrl（报工链接）和 ReportUrlOp（工序报工链接）三种。请选择其中一种有效类型。");

        string? baseUrl = string.IsNullOrWhiteSpace(dto.BaseUrl) ? null : dto.BaseUrl.Trim().TrimEnd('/');
        var needsBase = string.Equals(qrMode, "ReportUrl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(qrMode, "ReportUrlOp", StringComparison.OrdinalIgnoreCase);
        if (needsBase && !string.IsNullOrEmpty(baseUrl))
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw ThrowHelper.Biz(nameof(SaveAsync), "报工页打印地址请填写以http或https开头的完整网址。");
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
                throw ThrowHelper.Biz(nameof(SaveAsync), "自定义打印字段无效，或不属于本厂的工单字段。请到「工单字段」确认可用的自定义字段列表，选择合法字段。");
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
                $"打印自定义字段的设置格式读取失败。请检查设置内容的格式是否正确，参照说明修正后重新保存。");
        }
    }
}
