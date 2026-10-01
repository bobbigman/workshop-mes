using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Services;

/// <summary>知识库文件管理（docs/66）：上传 / 清单 / 原始文件预览 / 删除。只读预览与下载，不涉及 AI 翻译。</summary>
public interface IKnowledgeFileService
{
    /// <summary>PC 管理：某产品/工序已挂文件清单。</summary>
    Task<List<KnowledgeFileDto>> ListAsync(string refType, long refId, long factoryId);

    /// <summary>H5 工人：按工单（顺产品→工艺路线→工序）列文件清单。</summary>
    Task<List<KnowledgeFileDto>> ListByOrderAsync(long orderId, long factoryId);

    /// <summary>返回原始文件字节 + ContentType + 显示名（预览/下载）。</summary>
    Task<(byte[] Content, string ContentType, string FileName)> GetRawAsync(long fileId, long factoryId);

    /// <summary>上传文件并登记 base_knowledge_file。返回登记记录。</summary>
    Task<KnowledgeFileDto> UploadAsync(
        string refType, long refId, string? version,
        string originalFileName, byte[] content, long factoryId, long userId);

    /// <summary>删除登记记录（磁盘文件保留：同一文件可能被多处引用）。</summary>
    Task DeleteAsync(long fileId, long factoryId);
}

public class KnowledgeFileDto
{
    public long Id { get; set; }
    public string FileName { get; set; } = "";
    public string FileType { get; set; } = "";   // pdf/docx/xlsx/jpg/png/webp
    public string? Version { get; set; }
    public string RefType { get; set; } = "";    // product / operation
    public long RefId { get; set; }
    public string? RefName { get; set; }         // 挂工序时=工序名；挂产品时为空
}

public class KnowledgeFileService : IKnowledgeFileService
{
    private const long MaxFileBytes = 20 * 1024 * 1024; // 20MB
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public KnowledgeFileService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<List<KnowledgeFileDto>> ListAsync(string refType, long refId, long factoryId)
    {
        var type = NormalizeRefType(refType);
        await EnsureRefExistsAsync(type, refId, factoryId);
        var files = await _db.KnowledgeFiles.AsNoTracking()
            .Where(f => f.FactoryId == factoryId && f.RefType == type && f.RefId == refId)
            .OrderBy(f => f.Id)
            .ToListAsync();
        var opNames = await LoadOpNamesAsync(files, factoryId);
        return files.Select(f => ToDto(f, opNames)).ToList();
    }

    public async Task<List<KnowledgeFileDto>> ListByOrderAsync(long orderId, long factoryId)
    {
        var order = await _db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(ListByOrderAsync), "工单不存在");

        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == order.ProductId && p.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(ListByOrderAsync), "工单对应的产品不存在");

        var files = new List<BaseKnowledgeFile>();
        files.AddRange(await _db.KnowledgeFiles.AsNoTracking()
            .Where(f => f.FactoryId == factoryId && f.RefType == "product" && f.RefId == product.Id)
            .ToListAsync());

        var opIds = new List<long>();
        if (product.RoutingId != null)
        {
            opIds = await _db.RoutingSteps.AsNoTracking()
                .Where(s => s.RoutingId == product.RoutingId)
                .OrderBy(s => s.Seq)
                .Select(s => s.OperationId)
                .ToListAsync();
        }
        if (opIds.Count > 0)
        {
            files.AddRange(await _db.KnowledgeFiles.AsNoTracking()
                .Where(f => f.FactoryId == factoryId && f.RefType == "operation" && opIds.Contains(f.RefId))
                .ToListAsync());
        }

        var opNames = await LoadOpNamesAsync(files, factoryId);
        return files.Select(f => ToDto(f, opNames)).ToList();
    }

    public async Task<(byte[] Content, string ContentType, string FileName)> GetRawAsync(long fileId, long factoryId)
    {
        var f = await _db.KnowledgeFiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == fileId && x.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(GetRawAsync), "文件不存在或无权访问");

        var fullPath = ResolveFullPath(f.RelativePath);
        if (!File.Exists(fullPath))
            throw ThrowHelper.Biz(nameof(GetRawAsync), "文件已丢失，请联系管理员重新上传");

        var bytes = await File.ReadAllBytesAsync(fullPath);
        return (bytes, MapContentType(f.FileType), f.FileName);
    }

    public async Task<KnowledgeFileDto> UploadAsync(
        string refType, long refId, string? version,
        string originalFileName, byte[] content, long factoryId, long userId)
    {
        var type = NormalizeRefType(refType);
        await EnsureRefExistsAsync(type, refId, factoryId);

        var fileType = NormalizeFileType(Path.GetExtension(originalFileName));
        if (fileType == null)
            throw ThrowHelper.Biz(nameof(UploadAsync), "不支持的文件类型，请上传 pdf / docx / xlsx / jpg / png / webp");
        if (content.Length == 0)
            throw ThrowHelper.Biz(nameof(UploadAsync), "上传文件为空");
        if (content.Length > MaxFileBytes)
            throw ThrowHelper.Biz(nameof(UploadAsync), "文件超过 20MB 上限");

        var root = ResolveRoot();
        var relativePath = $"{factoryId}/{Guid.NewGuid():N}.{fileType}";
        var fullPath = Path.Combine(root, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(fullPath, content);

        var rec = new BaseKnowledgeFile
        {
            FactoryId = factoryId,
            FileName = string.IsNullOrWhiteSpace(originalFileName) ? "未命名文件" : originalFileName,
            RelativePath = relativePath,
            FileType = fileType,
            RefType = type,
            RefId = refId,
            Version = string.IsNullOrWhiteSpace(version) ? null : version.Trim(),
            CreatedBy = userId,
            CreatedAt = DateTime.Now
        };
        _db.KnowledgeFiles.Add(rec);
        await _db.SaveChangesAsync();
        return ToDto(rec, null);
    }

    public async Task DeleteAsync(long fileId, long factoryId)
    {
        var f = await _db.KnowledgeFiles
            .FirstOrDefaultAsync(x => x.Id == fileId && x.FactoryId == factoryId)
            ?? throw ThrowHelper.Biz(nameof(DeleteAsync), "文件不存在或无权访问");
        _db.KnowledgeFiles.Remove(f);
        await _db.SaveChangesAsync();
    }

    // ---- 内部 ----

    private async Task EnsureRefExistsAsync(string refType, long refId, long factoryId)
    {
        bool exists = refType == "product"
            ? await _db.Products.AsNoTracking().AnyAsync(x => x.Id == refId && x.FactoryId == factoryId)
            : await _db.Operations.AsNoTracking().AnyAsync(x => x.Id == refId && x.FactoryId == factoryId);
        if (!exists)
            throw ThrowHelper.Biz(nameof(EnsureRefExistsAsync), refType == "product" ? "产品不存在" : "工序不存在");
    }

    private async Task<Dictionary<long, string>> LoadOpNamesAsync(List<BaseKnowledgeFile> files, long factoryId)
    {
        var opIds = files.Where(f => f.RefType == "operation").Select(f => f.RefId).Distinct().ToList();
        if (opIds.Count == 0) return new Dictionary<long, string>();
        return await _db.Operations.AsNoTracking()
            .Where(o => o.FactoryId == factoryId && opIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Name);
    }

    private static KnowledgeFileDto ToDto(BaseKnowledgeFile f, Dictionary<long, string>? opNames) => new()
    {
        Id = f.Id,
        FileName = f.FileName,
        FileType = f.FileType,
        Version = f.Version,
        RefType = f.RefType,
        RefId = f.RefId,
        RefName = f.RefType == "operation" && opNames != null ? opNames.GetValueOrDefault(f.RefId) : null
    };

    private static string NormalizeRefType(string refType)
    {
        var t = (refType ?? "").Trim().ToLowerInvariant();
        if (t != "product" && t != "operation")
            throw ThrowHelper.Biz(nameof(NormalizeRefType), "refType 只能为 product 或 operation");
        return t;
    }

    /// <summary>扩展名白名单 + 归一化。返回 null 表示不支持。</summary>
    private static string? NormalizeFileType(string? ext)
    {
        return (ext ?? "").TrimStart('.').ToLowerInvariant() switch
        {
            "pdf" => "pdf",
            "docx" => "docx",
            "xlsx" => "xlsx",
            "jpg" or "jpeg" => "jpg",
            "png" => "png",
            "webp" => "webp",
            _ => null
        };
    }

    private static string MapContentType(string fileType) => fileType switch
    {
        "pdf" => "application/pdf",
        "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "jpg" => "image/jpeg",
        "png" => "image/png",
        "webp" => "image/webp",
        _ => "application/octet-stream"
    };

    private string ResolveRoot()
    {
        var root = (_config["KnowledgeBase:RootPath"] ?? "").Trim();
        if (string.IsNullOrEmpty(root))
            throw ThrowHelper.Biz(nameof(ResolveRoot), "未配置 KnowledgeBase:RootPath");
        return Path.GetFullPath(root);
    }

    /// <summary>相对路径解析为绝对路径，并做路径逃逸防护（只允许落在根目录内）。</summary>
    private string ResolveFullPath(string relativePath)
    {
        var root = ResolveRoot();
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw ThrowHelper.Biz(nameof(ResolveFullPath), "非法文件路径");
        return full;
    }
}
