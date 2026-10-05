using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

public interface IAbnormalService
{
    Task<ApiResult<object?>> CreateAsync(AbnormalCreateForm form, long userId, long factoryId);
    Task<ApiResult<PageResult<AbnormalListDto>>> QueryAsync(AbnormalQueryDto query, long factoryId);
    Task<ApiResult<object?>> ResolveAsync(long id, AbnormalResolveDto dto, long userId, long factoryId);
}

public class AbnormalCreateForm
{
    public byte Type { get; set; }
    public string Description { get; set; } = "";
    public long? WorkOrderId { get; set; }
    public IFormFile? Image { get; set; }
}

public class AbnormalResolveDto
{
    public string? HandleNote { get; set; }
}

public class AbnormalQueryDto
{
    public byte? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class AbnormalListDto
{
    public long Id { get; set; }
    public byte AbnormalType { get; set; }
    public string TypeLabel { get; set; } = "";
    public string Description { get; set; } = "";
    public string? ImagePath { get; set; }
    public long? WorkOrderId { get; set; }
    public string? OrderNo { get; set; }
    public long ReportedBy { get; set; }
    public string ReporterName { get; set; } = "";
    public DateTime ReportedAt { get; set; }
    public byte Status { get; set; }
    public long? HandledBy { get; set; }
    public string? HandlerName { get; set; }
    public DateTime? RecoveredAt { get; set; }
    public string? HandleNote { get; set; }
}

public class BoardAbnormalTickerDto
{
    public long Id { get; set; }
    public byte AbnormalType { get; set; }
    public string TypeLabel { get; set; } = "";
    public string Description { get; set; } = "";
    public string? OrderNo { get; set; }
    public DateTime ReportedAt { get; set; }
}

public class AbnormalService : IAbnormalService
{
    private static readonly HashSet<string> AllowedImageExt = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly WechatEventService? _events;

    public AbnormalService(AppDbContext db, IWebHostEnvironment env, WechatEventService? events = null)
    {
        _db = db;
        _env = env;
        _events = events;
    }

    public static string TypeLabel(byte type) => type switch
    {
        1 => "设备故障",
        2 => "物料短缺",
        3 => "质量异常",
        _ => "未知"
    };

    public async Task<ApiResult<object?>> CreateAsync(AbnormalCreateForm form, long userId, long factoryId)
    {
        if (form.Type is < 1 or > 3)
            throw ThrowHelper.Biz(nameof(CreateAsync), "异常类型无效，仅支持设备故障/物料短缺/质量异常");

        var desc = (form.Description ?? "").Trim();
        if (desc.Length == 0)
            throw ThrowHelper.Biz(nameof(CreateAsync), "请填写问题描述");
        if (desc.Length > 500)
            throw ThrowHelper.Biz(nameof(CreateAsync), "描述不能超过 500 字");

        if (form.WorkOrderId.HasValue)
        {
            var exists = await _db.WorkOrders.AsNoTracking()
                .AnyAsync(o => o.Id == form.WorkOrderId.Value && o.FactoryId == factoryId);
            if (!exists)
                throw ThrowHelper.Biz(nameof(CreateAsync), "关联工单不存在");
        }

        string? imagePath = null;
        if (form.Image != null && form.Image.Length > 0)
            imagePath = await SaveImageAsync(form.Image, factoryId);

        var row = new ProdAbnormal
        {
            FactoryId = factoryId,
            AbnormalType = form.Type,
            Description = desc,
            ImagePath = imagePath,
            WorkOrderId = form.WorkOrderId,
            ReportedBy = userId,
            ReportedAt = DateTime.Now,
            Status = 0
        };
        await using var tx = await _db.Database.BeginTransactionAsync();
        _db.Abnormals.Add(row);
        await _db.SaveChangesAsync();
        if (_events != null)
        {
            var reporter = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId && x.FactoryId == factoryId)
                ?? throw ThrowHelper.Biz(nameof(CreateAsync), "上报人不存在");
            await _events.EnqueueAbnormalAsync(row, reporter);
        }
        await tx.CommitAsync();
        return ApiResult<object?>.OkMsg();
    }

    public async Task<ApiResult<PageResult<AbnormalListDto>>> QueryAsync(AbnormalQueryDto query, long factoryId)
    {
        if (query.Page < 1) query.Page = 1;
        if (query.PageSize < 1 || query.PageSize > 100) query.PageSize = 20;

        var q = _db.Abnormals.AsNoTracking().Where(a => a.FactoryId == factoryId);
        if (query.Status.HasValue)
            q = q.Where(a => a.Status == query.Status.Value);

        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(a => a.ReportedAt).ThenByDescending(a => a.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync();

        var userIds = rows.Select(r => r.ReportedBy).Concat(rows.Where(r => r.HandledBy.HasValue).Select(r => r.HandledBy!.Value)).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name);

        var orderIds = rows.Where(r => r.WorkOrderId.HasValue).Select(r => r.WorkOrderId!.Value).Distinct().ToList();
        var orderNos = orderIds.Count == 0
            ? new Dictionary<long, string>()
            : await _db.WorkOrders.AsNoTracking()
                .Where(o => orderIds.Contains(o.Id) && o.FactoryId == factoryId)
                .ToDictionaryAsync(o => o.Id, o => o.OrderNo);

        var list = rows.Select(r => new AbnormalListDto
        {
            Id = r.Id,
            AbnormalType = r.AbnormalType,
            TypeLabel = TypeLabel(r.AbnormalType),
            Description = r.Description,
            ImagePath = r.ImagePath,
            WorkOrderId = r.WorkOrderId,
            OrderNo = r.WorkOrderId.HasValue ? orderNos.GetValueOrDefault(r.WorkOrderId.Value) : null,
            ReportedBy = r.ReportedBy,
            ReporterName = names.GetValueOrDefault(r.ReportedBy, ""),
            ReportedAt = r.ReportedAt,
            Status = r.Status,
            HandledBy = r.HandledBy,
            HandlerName = r.HandledBy.HasValue ? names.GetValueOrDefault(r.HandledBy.Value) : null,
            RecoveredAt = r.RecoveredAt,
            HandleNote = r.HandleNote
        }).ToList();

        return ApiResult<PageResult<AbnormalListDto>>.Ok(new PageResult<AbnormalListDto> { List = list, Total = total });
    }

    public async Task<ApiResult<object?>> ResolveAsync(long id, AbnormalResolveDto dto, long userId, long factoryId)
    {
        var row = await _db.Abnormals.FirstOrDefaultAsync(a => a.Id == id && a.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(ResolveAsync), "异常记录不存在");
        if (row.Status != 0)
            throw ThrowHelper.Biz(nameof(ResolveAsync), "该异常已处理");

        var note = string.IsNullOrWhiteSpace(dto.HandleNote) ? null : dto.HandleNote.Trim();
        if (note != null && note.Length > 256)
            throw ThrowHelper.Biz(nameof(ResolveAsync), "处理说明不能超过 256 字");

        row.Status = 1;
        row.HandledBy = userId;
        row.RecoveredAt = DateTime.Now;
        row.HandleNote = note;
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    private async Task<string> SaveImageAsync(IFormFile file, long factoryId)
    {
        if (file.Length > 5 * 1024 * 1024)
            throw ThrowHelper.Biz(nameof(SaveImageAsync), "图片不能超过 5MB");

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext) || !AllowedImageExt.Contains(ext))
            throw ThrowHelper.Biz(nameof(SaveImageAsync), "仅支持 jpg/png/gif/webp 图片");

        var webRoot = _env.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
            webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");

        var relDir = Path.Combine("uploads", "abnormal", factoryId.ToString());
        var absDir = Path.Combine(webRoot, relDir);
        Directory.CreateDirectory(absDir);

        var fileName = $"{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        var absPath = Path.Combine(absDir, fileName);
        await using (var stream = File.Create(absPath))
            await file.CopyToAsync(stream);

        return $"uploads/abnormal/{factoryId}/{fileName}".Replace('\\', '/');
    }
}
