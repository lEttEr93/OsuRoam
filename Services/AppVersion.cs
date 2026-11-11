using System.Reflection;

namespace OsuRoam.Services;

/// <summary>
/// 程序自身的版本号。取 <c>AssemblyInformationalVersion</c>（打包时写的是 1.5.6.2 这种），
/// 因为 <c>AssemblyVersion</c> 只有四段数字，表达不了 1.5.6fix 这类后缀。
///
/// SDK 默认会把 <c>+&lt;commit&gt;</c> 拼在 InformationalVersion 后面，这里切掉。
/// </summary>
public static class AppVersion
{
    public static string Display { get; } = read();

    /// <summary>User-Agent 里用的短版本（不含后缀以外的修饰）。</summary>
    public static string UserAgent => $"osu-roam/{Display}";

    private static string read()
    {
        string? info = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            int plus = info.IndexOf('+');
            return (plus >= 0 ? info[..plus] : info).Trim();
        }

        return typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
