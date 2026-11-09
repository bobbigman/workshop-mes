using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using ahu.MicrosoftMes.Common;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;

namespace ahu.MicrosoftMes.Mcp;

/// <summary>本厂当前 MCP 钥匙状态（每厂一把，不展示历史列表）。</summary>
public class McpKeyStatusDto
{
    public bool HasActive { get; set; }
    public long? KeyId { get; set; }
    public string KeyAlias { get; set; } = "";
    public DateTime? CreatedAt { get; set; }
    public DateTime? ExpireAt { get; set; }
    /// <summary>库里是否有明文，可随时查看/复制报文。旧钥匙无明文时须重新生成一次。</summary>
    public bool CanReveal { get; set; }
}

public class McpKeyCreateResultDto
{
    public long KeyId { get; set; }
    public string KeyAlias { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string McpUrl { get; set; } = "";
    public string FactoryLabel { get; set; } = "";
    public string ConnectionText { get; set; } = "";
    public bool RevokedOld { get; set; }
    public string Tip { get; set; } = "报文已保存，管理员可随时再复制";
}

public class McpKeyConnectionDto
{
    public long KeyId { get; set; }
    public string KeyAlias { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string McpUrl { get; set; } = "";
    public string FactoryLabel { get; set; } = "";
    public string ConnectionText { get; set; } = "";
}

public class McpKeyRevealDto
{
    /// <summary>当前登录管理员的登录密码（防旁人点「复制报文」）。</summary>
    public string? Password { get; set; }
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
    Task<ApiResult<McpKeyStatusDto>> GetStatusAsync(long factoryId);
    Task<ApiResult<McpKeyConnectionDto>> GetConnectionAsync(long factoryId, long userId, string? password, string mcpUrl);
    Task<ApiResult<McpKeyCreateResultDto>> CreateAsync(long factoryId, string mcpUrl);
    Task<ApiResult<object?>> RevokeActiveAsync(long factoryId);
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

    public async Task<ApiResult<McpKeyStatusDto>> GetStatusAsync(long factoryId)
    {
        if (factoryId <= 0)
            throw ThrowHelper.BizUser("无法确定当前操作的工厂。请确认已登录并选择了正确的工厂后再管理 MCP 访问钥匙。");

        var row = await _db.McpKeys.AsNoTracking()
            .Where(k => k.FactoryId == factoryId && k.Status == 1)
            .OrderByDescending(k => k.KeyId)
            .FirstOrDefaultAsync();

        if (row == null)
        {
            return ApiResult<McpKeyStatusDto>.Ok(new McpKeyStatusDto { HasActive = false });
        }

        return ApiResult<McpKeyStatusDto>.Ok(new McpKeyStatusDto
        {
            HasActive = true,
            KeyId = row.KeyId,
            KeyAlias = row.KeyAlias,
            CreatedAt = row.CreatedAt,
            ExpireAt = row.ExpireAt,
            CanReveal = !string.IsNullOrEmpty(row.ApiKeyPlain)
        });
    }

    public async Task<ApiResult<McpKeyConnectionDto>> GetConnectionAsync(long factoryId, long userId, string? password, string mcpUrl)
    {
        if (factoryId <= 0)
            throw ThrowHelper.BizUser("无法确定当前操作的工厂。请确认已登录并选择了正确的工厂后再管理 MCP 访问钥匙。");
        if (userId <= 0)
            throw ThrowHelper.BizUser("登录已失效，请重新登录后再复制报文。");
        if (string.IsNullOrWhiteSpace(password))
            throw ThrowHelper.BizUser("请输入当前账号的登录密码后再复制报文。");
        if (string.IsNullOrWhiteSpace(mcpUrl))
            throw ThrowHelper.Biz(nameof(GetConnectionAsync), "MCP 服务地址还没设置。请在后端设置文件 appsettings.json 中填写 Mcp:Url，或确认当前站点可通过 /mcp 路径访问服务。");

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.FactoryId == factoryId && u.Status == 1);
        if (user == null)
            throw ThrowHelper.BizUser("当前账号无效或已停用，请重新登录后再试。");
        if (!PasswordHelper.Verify(password, user.Password))
            throw ThrowHelper.BizUser("密码不正确。请输入当前登录账号的密码，不要把密码告诉别人。");

        var row = await _db.McpKeys.AsNoTracking()
            .Where(k => k.FactoryId == factoryId && k.Status == 1)
            .OrderByDescending(k => k.KeyId)
            .FirstOrDefaultAsync();
        if (row == null)
            throw ThrowHelper.BizUser("本厂还没有有效的 MCP 钥匙，请先生成报文。");
        if (string.IsNullOrEmpty(row.ApiKeyPlain))
            throw ThrowHelper.BizUser("这把钥匙是旧版生成的，库里没有明文，无法再查看。请点「重新生成」一次，之后即可随时复制（对接方需改用新报文）。");

        var factory = await _db.Factories.AsNoTracking().FirstOrDefaultAsync(f => f.Id == factoryId)
            ?? throw ThrowHelper.BizUser("工厂不存在");
        var url = mcpUrl.Trim().TrimEnd('/');
        var factoryLabel = FormatFactoryLabel(factory);
        var plain = row.ApiKeyPlain;

        return ApiResult<McpKeyConnectionDto>.Ok(new McpKeyConnectionDto
        {
            KeyId = row.KeyId,
            KeyAlias = row.KeyAlias,
            ApiKey = plain,
            McpUrl = url,
            FactoryLabel = factoryLabel,
            ConnectionText = BuildConnectionText(factoryLabel, url, plain)
        });
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
        {
            old.Status = 0;
            old.ApiKeyPlain = null; // 吊销后清明文，避免旧钥残留
        }

        var alias = $"{factory.FactoryName}-MCP";
        var plain = GenerateKey(32);
        var row = new SysMcpKey
        {
            FactoryId = factoryId,
            KeyAlias = alias,
            ApiKeyHash = PasswordHelper.Hash(plain),
            ApiKeyPlain = plain,
            Status = 1,
            ExpireAt = null,
            CreatedAt = DateTime.Now,
            Remark = null
        };
        var url = mcpUrl.Trim().TrimEnd('/');
        var factoryLabel = FormatFactoryLabel(factory);
        var connectionText = BuildConnectionText(factoryLabel, url, plain);

        _db.McpKeys.Add(row);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        _logger.LogInformation(
            "MCP 钥匙已生成 factory={FactoryName}({FactoryId}) alias={Alias} keyId={KeyId} revokedOld={RevokedOld}",
            factory.FactoryName, factory.Id, alias, row.KeyId, revokedOld);

        var tip = revokedOld
            ? "已吊销旧钥匙。请把新报文发给对接方更新；本页可随时再复制"
            : "报文已保存，管理员可随时再复制";

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

    public async Task<ApiResult<object?>> RevokeActiveAsync(long factoryId)
    {
        if (factoryId <= 0)
            throw ThrowHelper.BizUser("无法确定当前工厂");

        var rows = await _db.McpKeys
            .Where(k => k.FactoryId == factoryId && k.Status == 1)
            .ToListAsync();
        if (rows.Count == 0)
            return ApiResult<object?>.OkMsg();

        var factoryName = await _db.Factories.AsNoTracking()
            .Where(f => f.Id == factoryId)
            .Select(f => f.FactoryName)
            .FirstOrDefaultAsync() ?? "";

        foreach (var row in rows)
        {
            row.Status = 0;
            row.ApiKeyPlain = null;
            _logger.LogInformation(
                "MCP 钥匙已吊销 factory={FactoryName}({FactoryId}) alias={Alias} keyId={KeyId}",
                factoryName, factoryId, row.KeyAlias, row.KeyId);
        }
        await _db.SaveChangesAsync();
        return ApiResult<object?>.OkMsg();
    }

    private static string FormatFactoryLabel(SysFactory factory) =>
        string.IsNullOrWhiteSpace(factory.FactoryCode)
            ? factory.FactoryName
            : $"{factory.FactoryName} ({factory.FactoryCode})";

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

    private static string GenerateKey(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = KeyChars[RandomNumberGenerator.GetInt32(KeyChars.Length)];
        return new string(chars);
    }
}
