using System.IO;

namespace OsuRoam.Services;

/// <summary>
/// 便携式存储：配置/缓存默认放在**程序目录下的 data\**，跟 exe 一起走。
/// 程序目录不可写时才退回 %LOCALAPPDATA%\OsuRoam。
/// 首次运行时如果 data\ 还没有配置，会把旧位置的数据自动迁移过来。
/// 注意：这里不能调用 AppLog —— AppLog.LogDirectory 依赖本类，会无限递归。
/// </summary>
public static class StoragePaths
{
    private static string? root;
    private static string? note;

    public static string Root => root ??= resolve();

    /// <summary>最终选了哪个目录、为什么，供启动日志打印。</summary>
    public static string? Note => note;

    public static bool UsingPortableDirectory => root is not null &&
        root.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase);

    private static string resolve()
    {
        string portable = Path.Combine(AppContext.BaseDirectory, "data");
        if (isWritable(portable, out string portableError))
        {
            note = $"便携模式：{portable}";
            migrateLegacy(portable);
            return portable;
        }

        string legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsuRoam");
        if (isWritable(legacy, out string legacyError))
        {
            note = $"程序目录不可写（{portableError}），改用：{legacy}";
            return legacy;
        }

        note = $"程序目录与用户目录都不可写（{portableError} / {legacyError}），配置将无法保存";
        return legacy;
    }

    /// <summary>把旧位置（%LOCALAPPDATA%\OsuRoam）的配置类数据搬到便携目录，缓存不搬。</summary>
    private static void migrateLegacy(string target)
    {
        try
        {
            string legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsuRoam");
            if (string.Equals(legacy, target, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(legacy))
                return;
            if (File.Exists(Path.Combine(target, "settings.json")))
                return;

            int copied = 0;
            foreach (string name in new[] { "settings.json", "history.json", "patterns.json" })
            {
                string source = Path.Combine(legacy, name);
                if (!File.Exists(source))
                    continue;
                File.Copy(source, Path.Combine(target, name), overwrite: true);
                copied++;
            }

            if (copied > 0)
                note = $"{note}（已从旧位置迁移 {copied} 个配置文件）";
        }
        catch
        {
            // 迁移失败不致命：新目录里会生成默认配置。
        }
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
