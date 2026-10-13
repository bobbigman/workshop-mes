using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace ahu.MicrosoftMes.Common;

public class JwtOptions
{
    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public int ExpireHours { get; set; } = 12;
}

/// <summary>选厂票据内锁定的候选厂（docs/151）</summary>
public class SelectFactoryCandidateClaim
{
    public string FactoryCode { get; set; } = "";
    public long UserId { get; set; }
}

public static class JwtHelper
{
    public const string ClaimUserId = "uid";
    public const string ClaimFactoryId = "fid";
    public const string ClaimRole = "role";
    public const string ClaimName = "name";

    /// <summary>选厂短时票据 audience，与正式 JWT 隔离，不能当 Bearer 进业务接口（docs/151）</summary>
    public const string SelectFactoryAudience = "select-factory";
    public const string ClaimTokenType = "typ";
    public const string TokenTypeSelectFactory = "select-factory";
    public const string ClaimCandidates = "cands";
    public const int SelectFactoryExpireMinutes = 2;

    private static readonly JsonSerializerOptions SelectCandJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string CreateToken(JwtOptions opt, long userId, long factoryId, byte role, string name)
        => CreateToken(opt, userId, factoryId, role, name, opt.ExpireHours);

    /// <param name="expireHours">覆盖默认过期小时数（个人免密登录用更长有效期，docs/150）</param>
    public static string CreateToken(JwtOptions opt, long userId, long factoryId, byte role, string name, int expireHours)
    {
        if (string.IsNullOrWhiteSpace(opt.Secret) || opt.Secret.Length < 32)
            throw ThrowHelper.General(nameof(CreateToken), "Jwt:Secret 未配置或长度不足32位");
        if (expireHours < 1)
            throw ThrowHelper.General(nameof(CreateToken), "JWT 过期小时数无效");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opt.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimUserId, userId.ToString()),
            new Claim(ClaimFactoryId, factoryId.ToString()),
            new Claim(ClaimRole, role.ToString()),
            new Claim(ClaimName, name),
            new Claim(ClaimTypes.Role, role.ToString())
        };
        var token = new JwtSecurityToken(
            issuer: opt.Issuer,
            audience: opt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(expireHours),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>多厂登录第二步用：短命票据，payload 锁死合法 factoryCode→userId（docs/151）</summary>
    public static (string Token, string Jti) CreateSelectFactoryToken(
        JwtOptions opt,
        IReadOnlyList<SelectFactoryCandidateClaim> candidates)
    {
        if (string.IsNullOrWhiteSpace(opt.Secret) || opt.Secret.Length < 32)
            throw ThrowHelper.General(nameof(CreateSelectFactoryToken), "Jwt:Secret 未配置或长度不足32位");
        if (candidates == null || candidates.Count < 2)
            throw ThrowHelper.General(nameof(CreateSelectFactoryToken), "选厂候选不足，不应签发 selectToken");

        var jti = Guid.NewGuid().ToString("N");
        var candJson = JsonSerializer.Serialize(candidates, SelectCandJson);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opt.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Jti, jti),
            new Claim(ClaimTokenType, TokenTypeSelectFactory),
            new Claim(ClaimCandidates, candJson)
        };
        var token = new JwtSecurityToken(
            issuer: opt.Issuer,
            audience: SelectFactoryAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(SelectFactoryExpireMinutes),
            signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(token), jti);
    }

    /// <summary>校验选厂票据；失败抛业务异常。不走 Bearer 中间件。</summary>
    public static (string Jti, List<SelectFactoryCandidateClaim> Candidates) ValidateSelectFactoryToken(
        JwtOptions opt, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw ThrowHelper.BizUser("选厂凭证无效或已过期，请重新登录");
        if (string.IsNullOrWhiteSpace(opt.Secret) || opt.Secret.Length < 32)
            throw ThrowHelper.General(nameof(ValidateSelectFactoryToken), "Jwt:Secret 未配置或长度不足32位");

        var handler = new JwtSecurityTokenHandler();
        try
        {
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = opt.Issuer,
                ValidAudience = SelectFactoryAudience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opt.Secret)),
                ClockSkew = TimeSpan.FromSeconds(30)
            }, out _);

            var typ = principal.FindFirst(ClaimTokenType)?.Value;
            if (!string.Equals(typ, TokenTypeSelectFactory, StringComparison.Ordinal))
                throw ThrowHelper.BizUser("选厂凭证无效或已过期，请重新登录");

            var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            if (string.IsNullOrWhiteSpace(jti))
                throw ThrowHelper.BizUser("选厂凭证无效或已过期，请重新登录");

            var candRaw = principal.FindFirst(ClaimCandidates)?.Value;
            if (string.IsNullOrWhiteSpace(candRaw))
                throw ThrowHelper.BizUser("选厂凭证无效或已过期，请重新登录");

            var candidates = JsonSerializer.Deserialize<List<SelectFactoryCandidateClaim>>(candRaw, SelectCandJson);
            if (candidates == null || candidates.Count == 0)
                throw ThrowHelper.BizUser("选厂凭证无效或已过期，请重新登录");

            return (jti, candidates);
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception)
        {
            throw ThrowHelper.BizUser("选厂凭证无效或已过期，请重新登录");
        }
    }

    public static long GetUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirst(ClaimUserId)?.Value
            ?? throw ThrowHelper.BizUser("登录状态已失效，请重新登录");
        return long.Parse(v);
    }

    public static long GetFactoryId(ClaimsPrincipal user)
    {
        var v = user.FindFirst(ClaimFactoryId)?.Value
            ?? throw ThrowHelper.BizUser("登录状态已失效，请重新登录");
        return long.Parse(v);
    }

    public static byte GetRole(ClaimsPrincipal user)
    {
        var v = user.FindFirst(ClaimRole)?.Value
            ?? throw ThrowHelper.BizUser("登录状态已失效，请重新登录");
        return byte.Parse(v);
    }
}
