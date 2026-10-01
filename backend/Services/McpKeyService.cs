using System.Security.Cryptography;
using ahu.MicrosoftMes.Common;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Mcp;

public class McpKeyListItemDto
{
    public long KeyId { get; set; }
    public long FactoryId { get; set; }
    public string FactoryName { get; set; } = "";
    public string KeyAlias { get; set; } = "";
    public byte Status { get; set; }
    public DateTime? ExpireAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Remark { get; set; }
}

public class McpKeyCreateDto
{
    public long FactoryId { get; set; }
    public string? KeyAlias { get; set; }
    public DateTime? ExpireAt { get; set; }
    public string? Remark { get; set; }
}

public class McpKeyCreateResultDto
{
    public long KeyId { get; set; }
    public string KeyAlias { get; set; } = "";
    /// <summary>明文钥匙，仅创建时返回一次。</summary>
    public string ApiKey { get; set; } = "";
    public string Tip { get; set; } = "请立即复制发给客户，关闭后不再显示";
}

public class McpKeyFactoryOptionDto
{
    public long Id { get; set; }
    public string FactoryCode { get; set; } = "";
    public string FactoryName { get; set; } = "";
}

public interface IMcpKeyService
{
    Task<ApiResult<List<McpKeyFactoryOptionDto>>> ListFactoriesAsync();
    Task<ApiResult<List<McpKeyListItemDto>>> ListAsync(long? factoryId);
    Task<ApiResult<McpKeyCreateResultDto>> CreateAsync(McpKeyCreateDto dto);
    Task<ApiResult<object?>> RevokeAsync(long keyId);
}

public class McpKeyService : IMcpKeyService
{
    private const string KeyChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private readonly AppDbContext _db;
    private readonly ILogger<McpKeyService> _logger;

    public McpKeyService(AppDbContext db, ILogger<McpKeyService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ApiResult<List<McpKeyFactoryOptionDto>>> ListFactoriesAsync()
    {
        var list = await _db.Factories.AsNoTracking()
            .OrderBy(f => f.Id)
            .Select(f => new McpKeyFactoryOptionDto
            {
                Id = f.Id,
                FactoryCode = f.FactoryCode,
                FactoryName = f.FactoryName
            })
            .ToListAsync();
        return ApiResult<List<McpKeyFactoryOptionDto>>.Ok(list);
    }

    public async Task<ApiResult<List<McpKeyListItemDto>>> ListAsync(long? factoryId)
    {
        var q = from k in _db.McpKeys.AsNoTracking()
                join f in _db.Factories.AsNoTracking() on k.FactoryId equals f.Id
                select new { k, f.FactoryName };
        if (factoryId is > 0)
            q = q.Where(x => x.k.FactoryId == factoryId.Value);

        var list = await q.OrderByDescending(x => x.k.KeyId)
            .Select(x => new McpKeyListItemDto
            {
                KeyId = x.k.KeyId,
                FactoryId = x.k.FactoryId,
                FactoryName = x.FactoryName,
                KeyAlias = x.k.KeyAlias,
                Status = x.k.Status,
                ExpireAt = x.k.ExpireAt,
                CreatedAt = x.k.CreatedAt,
                Remark = x.k.Remark
            })
            .ToListAsync();
        return ApiResult<List<McpKeyListItemDto>>.Ok(list);
    }

    public async Task<ApiResult<McpKeyCreateResultDto>> CreateAsync(McpKeyCreateDto dto)
    {
        if (dto.FactoryId <= 0)
            throw ThrowHelper.BizUser("请先选择工厂");

        var factory = await _db.Factories.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == dto.FactoryId);
        if (factory == null)
            throw ThrowHelper.BizUser("工厂不存在");

        var alias = (dto.KeyAlias ?? "").Trim();
        if (string.IsNullOrEmpty(alias))
            alias = $"{factory.FactoryName}-MCP";

        var plain = GenerateKey(32);
        var row = new SysMcpKey
        {
            FactoryId = dto.FactoryId,
            KeyAlias = alias,
            ApiKeyHash = PasswordHelper.Hash(plain),
            Status = 1,
            ExpireAt = dto.ExpireAt,
            CreatedAt = DateTime.Now,
            Remark = string.IsNullOrWhiteSpace(dto.Remark) ? null : dto.Remark.Trim()
        };
        _db.McpKeys.Add(row);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "MCP 钥匙已生成 factory={FactoryName}({FactoryId}) alias={Alias} keyId={KeyId}",
            factory.FactoryName, factory.Id, alias, row.KeyId);

        return ApiResult<McpKeyCreateResultDto>.Ok(new McpKeyCreateResultDto
        {
            KeyId = row.KeyId,
            KeyAlias = alias,
            ApiKey = plain
        });
    }

    public async Task<ApiResult<object?>> RevokeAsync(long keyId)
    {
        var row = await _db.McpKeys.FirstOrDefaultAsync(k => k.KeyId == keyId);
        if (row == null)
            throw ThrowHelper.BizUser("钥匙不存在");
        if (row.Status == 0)
            return ApiResult<object?>.OkMsg();

        var factoryName = await _db.Factories.AsNoTracking()
            .Where(f => f.Id == row.FactoryId)
            .Select(f => f.FactoryName)
            .FirstOrDefaultAsync() ?? "";

        row.Status = 0;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "MCP 钥匙已吊销 factory={FactoryName}({FactoryId}) alias={Alias} keyId={KeyId}",
            factoryName, row.FactoryId, row.KeyAlias, row.KeyId);

        return ApiResult<object?>.OkMsg();
    }

    private static string GenerateKey(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = KeyChars[RandomNumberGenerator.GetInt32(KeyChars.Length)];
        return new string(chars);
    }
}
