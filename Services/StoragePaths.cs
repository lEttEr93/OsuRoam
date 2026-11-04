using System.IO;
using System.Linq;
using System.Text.Json;

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
            // 每次解压新包都是空库的话，键型排序永远热不起来（现场：新目录起步 5 条，
            // 而旧目录里攒了几百条）。第一次运行时从更丰富的旧目录继承一份。
            inheritPatternLibrary(portable);
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

    /// <summary>
    /// 继承更丰富的键型库：当前 <c>patterns.json</c> 条目很少时，从「同名程序的其它目录」
    /// 和旧位置里找一份最大的复制过来。键型库是纯派生数据，复制不会有副作用。
    /// </summary>
    private static void inheritPatternLibrary(string target)
    {
        try
        {
            string targetFile = Path.Combine(target, "patterns.json");
            int current = countPatterns(targetFile);
            if (current >= InheritThreshold)
                return;

            string? best = null;
            int bestCount = current;
            foreach (string candidate in candidateLibraries())
            {
                int count = countPatterns(candidate);
                if (count <= bestCount)
                    continue;
                best = candidate;
                bestCount = count;
            }
            if (best is null)
                return;

            File.Copy(best, targetFile, overwrite: true);
            note = $"{note}（键型库继承自 {Path.GetDirectoryName(best)}：{bestCount} 条）";
        }
        catch
        {
            // 继承失败不影响启动，库会自己慢慢长。
        }
    }

    /// <summary>当前库条目少于这个数时才考虑继承。</summary>
    private const int InheritThreshold = 50;

    /// <summary>可能存在的旧键型库：程序所在盘根目录下同名前缀的目录，以及旧位置。</summary>
    private static IEnumerable<string> candidateLibraries()
    {
        string baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string? parent = Path.GetDirectoryName(baseDirectory);
        if (parent is not null)
        {
            string[] directories;
            try
            {
                directories = Directory.GetDirectories(parent);
            }
            catch
            {
                directories = [];
            }
            foreach (string directory in directories.Take(120))
            {
                string name = Path.GetFileName(directory);
                if (!name.StartsWith("osu-roam", StringComparison.OrdinalIgnoreCase))
                    continue;
                yield return Path.Combine(directory, "data", "patterns.json");
            }
        }
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsuRoam", "patterns.json");
    }

    /// <summary>数一下 patterns.json 里有多少条（读不出来按 0 算）。</summary>
    private static int countPatterns(string path)
    {
        try
        {
            if (!File.Exists(path))
                return 0;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.EnumerateObject().Count() : 0;
        }
        catch
        {
            return 0;
        }
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
