using System.IO;

namespace OsuRoam.Services;

/// <summary>
/// 配置/缓存位置：默认仍然是 %LOCALAPPDATA%\OsuRoam（和以前一致）。
/// 只有当那个位置不可写时才退到程序目录下的 data\，避免程序在受限环境里
/// 完全起不来。注意：这里不能调用 AppLog —— AppLog.LogDirectory 依赖本类。
/// </summary>
public static class StoragePaths
{
    private static string? root;
    private static string? note;

    public static string Root => root ??= resolve();

    /// <summary>最终选了哪个目录、为什么，供启动日志打印。</summary>
    public static string? Note => note;

    private static string resolve()
    {
        string preferred = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsuRoam");
        if (isWritable(preferred, out _))
        {
            note = $"使用用户目录：{preferred}";
            return preferred;
        }

        string fallback = Path.Combine(AppContext.BaseDirectory, "data");
        if (isWritable(fallback, out string fallbackError))
        {
            note = $"用户目录不可写，改用程序目录：{fallback}";
            return fallback;
        }

        note = $"用户目录与程序目录都不可写（{fallbackError}），配置将无法保存";
        return preferred;
    }

    private static bool isWritable(string directory, out string error)
    {
        error = string.Empty;
        try
        {
            Directory.CreateDirectory(directory);
            string probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }
}
