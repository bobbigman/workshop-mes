using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
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

public class McpKeyCreateResultDto
{
    public long KeyId { get; set; }
    public string KeyAlias { get; set; } = "";
    /// <summary>明文钥匙，仅创建时返回一次。</summary>
    public string ApiKey { get; set; } = "";
    /// <summary>MCP 连接地址（来自 Mcp:Url 或当前站点 /mcp）。</summary>
    public string McpUrl { get; set; } = "";
    /// <summary>账套展示，如「HY伞业 (HY001)」。</summary>
    public string FactoryLabel { get; set; } = "";
    /// <summary>完整连接报文，整段可复制。</summary>
    public string ConnectionText { get; set; } = "";
    /// <summary>本次是否先吊销了本厂旧有效钥匙。</summary>
    public bool RevokedOld { get; set; }
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
    Task<ApiResult<List<McpKeyFactoryOptionDto>>> ListFactoriesAsync(long factoryId);
    Task<ApiResult<List<McpKeyListItemDto>>> ListAsync(long factoryId);
    Task<ApiResult<McpKeyCreateResultDto>> CreateAsync(long factoryId, string mcpUrl);
    Task<ApiResult<object?>> RevokeAsync(long keyId, long factoryId);
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

    public async Task<ApiResult<List<McpKeyFactoryOptionDto>>> ListFactoriesAsync(long factoryId)
    {
        var list = await _db.Factories.AsNoTracking().Where(f => f.Id == factoryId && f.TrialCleanedAtUtc == null)
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

    public async Task<ApiResult<List<McpKeyListItemDto>>> ListAsync(long factoryId)
    {
        if (factoryId <= 0)
            throw ThrowHelper.BizUser("无法确定当前操作的工厂。请确认已登录并选择了正确的工厂后再管理 MCP 访问钥匙。");

        var list = await (
            from k in _db.McpKeys.AsNoTracking()
            join f in _db.Factories.AsNoTracking() on k.FactoryId equals f.Id
            where k.FactoryId == factoryId
            orderby k.KeyId descending
            select new McpKeyListItemDto
            {
                KeyId = k.KeyId,
                FactoryId = k.FactoryId,
                FactoryName = f.FactoryName,
                KeyAlias = k.KeyAlias,
                Status = k.Status,
                ExpireAt = k.ExpireAt,
                CreatedAt = k.CreatedAt,
                Remark = k.Remark
            }).ToListAsync();
        return ApiResult<List<McpKeyListItemDto>>.Ok(list);
    }

    public async Task<ApiResult<McpKeyCreateResultDto>> CreateAsync(long factoryId, string mcpUrl)
    {
        if (factoryId <= 0)
            throw ThrowHelper.BizUser("无法确定当前工厂");
        if (string.IsNullOrWhiteSpace(mcpUrl))
            throw ThrowHelper.Biz(nameof(CreateAsync), "MCP 服务地址还没设置。请在后端设置文件 appsettings.json 中填写 Mcp:Url，或确认当前站点可通过 /mcp 路径访问服务。");

        // 锁住已有工厂行，覆盖本厂尚无钥匙的情况；同厂跨进程请求也串行生成。
        // 吊销与新增共用事务，失败时旧钥匙不会被单独吊销。
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var factory = await _db.Factories
            .FromSqlInterpolated($"SELECT * FROM sys_factory WITH (UPDLOCK, HOLDLOCK) WHERE id = {factoryId}")
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == factoryId);
        if (factory == null)
            throw ThrowHelper.BizUser("工厂不存在");

        var oldKeys = await _db.McpKeys
            .Where(k => k.FactoryId == factoryId && k.Status == 1)
            .ToListAsync();
        var revokedOld = oldKeys.Count > 0;
        foreach (var old in oldKeys)
            old.Status = 0;

        var alias = $"{factory.FactoryName}-MCP";
        var plain = GenerateKey(32);
        var row = new SysMcpKey
        {
            FactoryId = factoryId,
            KeyAlias = alias,
            ApiKeyHash = PasswordHelper.Hash(plain),
            Status = 1,
            ExpireAt = null,
            CreatedAt = DateTime.Now,
            Remark = null
        };
        var url = mcpUrl.Trim().TrimEnd('/');
        var factoryLabel = string.IsNullOrWhiteSpace(factory.FactoryCode)
            ? factory.FactoryName
            : $"{factory.FactoryName} ({factory.FactoryCode})";
        var connectionText = BuildConnectionText(factoryLabel, url, plain);

        _db.McpKeys.Add(row);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        _logger.LogInformation(
            "MCP 钥匙已生成 factory={FactoryName}({FactoryId}) alias={Alias} keyId={KeyId} revokedOld={RevokedOld}",
            factory.FactoryName, factory.Id, alias, row.KeyId, revokedOld);

        var tip = revokedOld
            ? "已吊销旧钥匙，客户需改用新报文。请立即复制发给客户，关闭后不再显示"
            : "请立即复制发给客户，关闭后不再显示";

        return ApiResult<McpKeyCreateResultDto>.Ok(new McpKeyCreateResultDto
        {
            KeyId = row.KeyId,
            KeyAlias = alias,
            ApiKey = plain,
            McpUrl = url,
            FactoryLabel = factoryLabel,
            ConnectionText = connectionText,
            RevokedOld = revokedOld,
            Tip = tip
        });
    }

    private static string BuildConnectionText(string factoryLabel, string url, string plain) =>
        JsonSerializer.Serialize(new
        {
            mcpServers = new Dictionary<string, object>
            {
                [factoryLabel] = new
                {
                    type = "streamable-http",
                    url,
                    headers = new Dictionary<string, string>
                    {
                        ["Authorization"] = $"Bearer {plain}"
                    }
                }
            }
        }, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        });

    public async Task<ApiResult<object?>> RevokeAsync(long keyId, long factoryId)
    {
        if (factoryId <= 0)
            throw ThrowHelper.BizUser("无法确定当前工厂");

        var row = await _db.McpKeys.FirstOrDefaultAsync(k => k.KeyId == keyId && k.FactoryId == factoryId);
        if (row == null)
            throw ThrowHelper.BizUser("要撤销的访问钥匙不存在或已被撤销。请刷新钥匙列表确认目标钥匙是否还在。");
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
