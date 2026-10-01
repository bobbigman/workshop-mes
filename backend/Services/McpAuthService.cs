using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Models;
using ModelContextProtocol;

namespace ahu.MicrosoftMes.Services;

/// <summary>有效授权信息（供工具读取）</summary>
public class McpAuthInfo
{
    public string AuthToken { get; set; } = "";
    public string? KingdeeUserName { get; set; }
    public bool IsFinance { get; set; }
}

/// <summary>
/// MCP 配对码授权（M3，docs/25）：
///   1. CreateCodeAsync 生成一次性配对码（H5 链接携带）；
///   2. ExchangeAsync 校验 code → 生成 auth_token → 绑定金蝶用户；
///   3. ResolveAsync/RequireAuth 校验 auth_token，供各工具判断「是否已授权 + 是否财务」。
/// </summary>
public interface IMcpAuthService
{
    Task<string> CreateCodeAsync(long factoryId);
    /// <summary>打开 H5 前预检配对码（未使用、未过期）；失败抛业务异常。</summary>
    Task PeekCodeAsync(string code, long factoryId);
    /// <summary>按配对码反查所属厂（多租户钥匙定厂后，H5 页不再读配置 FactoryId）。</summary>
    Task<long> ResolveFactoryIdByCodeAsync(string code);
    Task<McpAuthInfo?> ExchangeAsync(string code, KingdeeUserInfo user, long factoryId);
    Task<McpAuthInfo?> ResolveAsync(string? authToken);
    Task<McpAuthInfo> RequireAuthAsync(string? authToken, string toolName);
}

public class McpAuthService : IMcpAuthService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public McpAuthService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<string> CreateCodeAsync(long factoryId)
    {
        var now = DateTime.Now;
        var codeExpireMinutes = _config.GetValue<int>("Mcp:CodeExpireMinutes", 5);

        // 一次性配对码（8 位，排除易混淆字符，便于手抄）
        var code = GenerateShortCode();
        // token 先占位生成，exchange 时才真正绑定金蝶用户
        var token = GenerateShortCode();

        _db.McpAuths.Add(new SysMcpAuth
        {
            FactoryId = factoryId,
            AuthCode = code,
            AuthToken = token,
            CodeUsed = false,
            CodeExpire = now.AddMinutes(codeExpireMinutes),
            TokenExpire = now.AddHours(2), // token 默认 2 小时，exchange 后按需调整
            CreatedAt = now
        });
        await _db.SaveChangesAsync();
        return code;
    }

    public async Task<long> ResolveFactoryIdByCodeAsync(string code)
    {
        code = (code ?? "").Trim();
        if (string.IsNullOrEmpty(code))
            throw ThrowHelper.Biz(nameof(ResolveFactoryIdByCodeAsync), "配对码无效。请回到豆包重新发起授权。");
        var factoryId = await _db.McpAuths.AsNoTracking()
            .Where(x => x.AuthCode == code)
            .Select(x => (long?)x.FactoryId)
            .FirstOrDefaultAsync();
        if (factoryId is null or <= 0)
            throw ThrowHelper.Biz(nameof(ResolveFactoryIdByCodeAsync), "配对码无效。请回到豆包再说一次「查工单」，打开新链接（旧链接只有约半小时有效）。");
        return factoryId.Value;
    }

    public async Task PeekCodeAsync(string code, long factoryId)
    {
        code = (code ?? "").Trim();
        var now = DateTime.Now;
        var row = await _db.McpAuths
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.AuthCode == code && x.FactoryId == factoryId);
        if (row == null)
            throw ThrowHelper.Biz(nameof(PeekCodeAsync), "配对码无效。请回到豆包再说一次「查工单」，打开新链接（旧链接只有约半小时有效）。");
        if (row.CodeUsed)
            throw ThrowHelper.Biz(nameof(PeekCodeAsync), "配对码已使用。请回到豆包重新发起授权，打开新链接。");
        if (row.CodeExpire < now)
            throw ThrowHelper.Biz(nameof(PeekCodeAsync), "配对码已过期。请回到豆包再说一次「查工单」，打开新链接后再填账号密码。");
    }

    public async Task<McpAuthInfo?> ExchangeAsync(string code, KingdeeUserInfo user, long factoryId)
    {
        code = (code ?? "").Trim();
        var now = DateTime.Now;
        var tokenExpireHours = _config.GetValue<int>("Mcp:TokenExpireHours", 2);

        var row = await _db.McpAuths
            .FirstOrDefaultAsync(x => x.AuthCode == code && x.FactoryId == factoryId);
        if (row == null)
            throw ThrowHelper.Biz(nameof(ExchangeAsync), "配对码无效。请回到豆包重新发起授权。");
        if (row.CodeUsed)
            throw ThrowHelper.Biz(nameof(ExchangeAsync), "配对码已使用，请重新发起授权");
        if (row.CodeExpire < now)
            throw ThrowHelper.Biz(nameof(ExchangeAsync), "配对码已过期，请重新发起授权");

        // 换新 token（覆盖占位值），绑定金蝶用户，标记 code 已用
        row.AuthToken = GenerateShortCode();
        row.KingdeeUserId = user.UserId;
        row.KingdeeUserName = user.Name;
        row.IsFinance = user.IsFinance;
        row.CodeUsed = true;
        row.TokenExpire = now.AddHours(tokenExpireHours);
        await _db.SaveChangesAsync();

        return new McpAuthInfo
        {
            AuthToken = row.AuthToken,
            KingdeeUserName = row.KingdeeUserName,
            IsFinance = row.IsFinance
        };
    }

    public async Task<McpAuthInfo?> ResolveAsync(string? authToken)
    {
        if (string.IsNullOrWhiteSpace(authToken)) return null;
        var token = authToken.Trim();
        var now = DateTime.Now;

        var row = await _db.McpAuths.AsNoTracking()
            .FirstOrDefaultAsync(x => x.AuthToken == token);
        // token 有效 = 已绑定金蝶用户（exchange 过）且未过期
        if (row == null || string.IsNullOrEmpty(row.KingdeeUserId) || row.TokenExpire < now)
            return null;

        return new McpAuthInfo
        {
            AuthToken = row.AuthToken,
            KingdeeUserName = row.KingdeeUserName,
            IsFinance = row.IsFinance
        };
    }

    /// <summary>校验授权，未授权/过期直接抛业务异常（带工具名定位）。</summary>
    public async Task<McpAuthInfo> RequireAuthAsync(string? authToken, string toolName)
    {
        var auth = await ResolveAsync(authToken);
        if (auth == null)
            throw new McpException($"{toolName}：未授权或授权已过期，请先发起金蝶授权");
        return auth;
    }

    private static string GenerateShortCode()
    {
        // 8 位，排除 0/O/1/I 等易混淆字符
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return RandomNumberGenerator.GetString(chars, 8);
    }
}
