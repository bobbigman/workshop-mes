namespace ahu.MicrosoftMes.Common;

public static class PasswordHelper
{
    public const int MinLength = 6;

    /// <summary>
    /// 密码只校验长度：至少 6 位。不限制大小写、数字、符号——用户输什么就是什么。
    /// </summary>
    public static void EnsureComplexity(string password, string where)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            throw ThrowHelper.Biz(where, $"密码至少 {MinLength} 位");
    }

    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password);

    public static bool Verify(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);
}
