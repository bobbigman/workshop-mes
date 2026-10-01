using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ahu.MicrosoftMes.Common;

public class JwtOptions
{
    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public int ExpireHours { get; set; } = 12;
}

public static class JwtHelper
{
    public const string ClaimUserId = "uid";
    public const string ClaimFactoryId = "fid";
    public const string ClaimRole = "role";
    public const string ClaimName = "name";

    public static string CreateToken(JwtOptions opt, long userId, long factoryId, byte role, string name)
    {
        if (string.IsNullOrWhiteSpace(opt.Secret) || opt.Secret.Length < 32)
            throw ThrowHelper.General(nameof(CreateToken), "Jwt:Secret 未配置或长度不足32位");

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
            expires: DateTime.UtcNow.AddHours(opt.ExpireHours),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
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
