namespace ahu.MicrosoftMes.Common;

using System.Reflection;

/// <summary>给人看的发布版本号。来自 csproj 的 Version（编译时自动 +1 patch）。</summary>
public static class AppVersion
{
    public static string Current { get; } = Read();

    private static string Read()
    {
        var asm = typeof(AppVersion).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var v = info;
            var plus = v.IndexOf('+');
            if (plus >= 0) v = v[..plus];
            var dash = v.IndexOf('-');
            if (dash >= 0) v = v[..dash];
            return v.Trim();
        }

        var ver = asm.GetName().Version;
        return ver == null ? "0.0.0" : $"{ver.Major}.{ver.Minor}.{ver.Build}";
    }
}
