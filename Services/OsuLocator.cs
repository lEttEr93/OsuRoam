using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace OsuRoam.Services;

/// <summary>客户端类型。和设置里存的 <c>lazer</c> / <c>stable</c> 一一对应。</summary>
public enum OsuClientKind
{
    Stable,
    Lazer
}

/// <summary>
/// 找到的 osu! 安装。**同时**记下「怎么找到的」，出问题时日志和界面能说清楚，
/// 不用再让用户猜是路径脏了还是没装。
/// </summary>
public sealed record OsuInstallation(OsuClientKind Client, string Executable, string Source)
{
    public string? Directory => Path.GetDirectoryName(Executable);

    public string ClientName => Client == OsuClientKind.Lazer ? "osu!lazer" : "osu!stable";

    public string ClientTag => Client == OsuClientKind.Lazer ? "lazer" : "stable";
}

/// <summary>
/// osu! 安装定位。老版本只按「客户端下拉框 + 三个固定目录」找 exe：
/// 客户端留空（旧配置被清过就是这样）就找不到，退化成「用系统关联打开 .osz」，
/// 而 `.osz` 在没有默认关联的机器上什么都不会发生 —— 表现就是「点了导入没反应」。
///
/// 这里改成多路探测，任一路命中都能用：
/// 1. 设置里手填的 exe；
/// 2. **正在运行的进程**（`osu!` 进程，按路径判断是 stable 还是 lazer）；
/// 3. **注册表协议处理程序**：lazer 注册 `osu.Uri.osu` / `osu.File.osz`，
///    stable 注册 `osustable.Uri.osu` / `osustable.File.osz`，都带完整 exe 路径；
/// 4. **卸载项**：`DisplayIcon` / `InstallLocation`（lazer 的是 `…\osulazer\current\osu!.exe`）；
/// 5. 常见安装目录。
///
/// 探测顺序按「可信度」排：手填 &gt; 正在跑 &gt; 协议注册 &gt; 卸载项 &gt; 常见目录。
/// 设置里挑了某个客户端时优先给那个，但**另一个装了就照样能用** ——
/// 客户端选错不该让导入彻底失败。
/// </summary>
public static class OsuLocator
{
    public const string AutoClient = "auto";

    private const string LazerProtocol = @"Software\Classes\osu.Uri.osu\shell\open\command";
    private const string LazerFileProtocol = @"Software\Classes\osu.File.osz\shell\open\command";
    private const string StableProtocol = @"Software\Classes\osustable.Uri.osu\shell\open\command";
    private const string StableFileProtocol = @"Software\Classes\osustable.File.osz\shell\open\command";
    private const string LegacyProtocol = @"Software\Classes\osu\shell\open\command";

    private static readonly object cacheLock = new();
    private static readonly Dictionary<OsuClientKind, OsuInstallation?> cachedKinds = [];
    private static (string Client, string Executable, OsuInstallation? Result)? cached;

    /// <summary>
    /// 定位 osu!。<paramref name="clientSetting"/> 是设置里的值（<c>lazer</c> / <c>stable</c> / 空 / <c>auto</c>）。
    /// 找不到返回 null。
    /// </summary>
    public static OsuInstallation? Detect(string? clientSetting, string? configuredExecutable)
    {
        string normalizedClient = NormalizeClient(clientSetting);
        string configured = BeatmapDownloadService.NormalizeExecutablePath(configuredExecutable);

        lock (cacheLock)
        {
            // 同一个设置下的结果缓存住：导入一次要问好几次（状态栏、日志、导入），
            // 而进程枚举 + 注册表读取是毫秒级但不该反复做。
            if (cached is { } hit &&
                string.Equals(hit.Client, normalizedClient, StringComparison.Ordinal) &&
                string.Equals(hit.Executable, configured, StringComparison.OrdinalIgnoreCase))
            {
                return hit.Result;
            }

            OsuInstallation? result = probe(normalizedClient, configured);
            cached = (normalizedClient, configured, result);
            return result;
        }
    }

    /// <summary>
    /// 只找某一种客户端（扫谱面目录时两种都要，stable 的 Songs 和 lazer 的 files 可能同时存在）。
    /// </summary>
    public static OsuInstallation? DetectByKind(OsuClientKind kind)
    {
        lock (cacheLock)
        {
            if (cachedKinds.TryGetValue(kind, out OsuInstallation? hit))
                return hit;

            List<Candidate> candidates = collectCandidates(string.Empty);
            OsuInstallation? result = pick(candidates.Where(candidate => candidate.Kind == kind).ToList(), kind);
            cachedKinds[kind] = result;
            return result;
        }
    }

    /// <summary>设置清空/换机器后重算一次（界面上的「重新检测」也走这里）。</summary>
    public static void Invalidate()
    {
        lock (cacheLock)
        {
            cached = null;
            cachedKinds.Clear();
        }
    }

    /// <summary>设置值的归一化：空字符串按「自动」处理 —— 旧配置就是这么坏的。</summary>
    public static string NormalizeClient(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "stable" or "osu!stable" or "osu!" => "stable",
        "lazer" or "osu!lazer" => "lazer",
        _ => AutoClient
    };

    /// <summary>从 exe 路径推断客户端：lazer 的目录叫 osulazer，Velopack 安装的真实 exe 在 <c>current\</c> 下。</summary>
    public static OsuClientKind InferClient(string executablePath)
    {
        string lower = executablePath.Replace('/', '\\').ToLowerInvariant();
        if (lower.Contains("osulazer", StringComparison.Ordinal) ||
            lower.Contains("\\current\\osu!.exe", StringComparison.Ordinal) ||
            lower.Contains("osu!lazer", StringComparison.Ordinal))
        {
            return OsuClientKind.Lazer;
        }

        return OsuClientKind.Stable;
    }

    /// <summary>把 <c>"C:\osu!\osu!.exe" "%1"</c> 这种注册表命令行拆成 exe 路径。</summary>
    public static string? ParseCommandLinePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        string text = command.Trim();
        string candidate;
        if (text.StartsWith('"'))
        {
            int closing = text.IndexOf('"', 1);
            candidate = closing > 1 ? text[1..closing] : text.Trim('"');
        }
        else
        {
            // 没引号时只取第一个参数：`C:\Program Files\osu!\osu!.exe %1` 里空格属于路径，
            // 所以按 ".exe" 截断比按空格切更靠谱。
            int exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            candidate = exe >= 0 ? text[..(exe + 4)] : text.Split(' ')[0];
        }

        candidate = candidate.Trim().Trim('"');
        return candidate.Length == 0 ? null : candidate;
    }

    /// <summary>
    /// lazer 的便携安装会把数据目录写在 <c>storage.ini</c> 的 <c>[Storage] FullPath</c>。
    /// 解析失败就返回 null（按默认位置继续找）。
    /// </summary>
    internal static string? ParseStorageIniPath(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        string section = string.Empty;
        foreach (string raw in content.Split('\n'))
        {
            string line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line[0] is ';' or '#')
                continue;
            if (line[0] == '[')
            {
                section = line.Trim('[', ']').Trim();
                continue;
            }

            if (!section.Equals("Storage", StringComparison.OrdinalIgnoreCase))
                continue;

            int separator = line.IndexOf('=');
            if (separator <= 0)
                continue;
            if (!line[..separator].Trim().Equals("FullPath", StringComparison.OrdinalIgnoreCase))
                continue;

            string value = line[(separator + 1)..].Trim().Trim('"');
            return value.Length == 0 ? null : value;
        }

        return null;
    }

    /// <summary>
    /// 谱面目录（去重后只留存在的）。stable 是 <c>Songs</c>，lazer 是内容寻址仓库 <c>files</c>。
    /// </summary>
    public static IReadOnlyList<string> BeatmapRoots(OsuInstallation? stable, OsuInstallation? lazer) =>
        BeatmapRoots(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            stable?.Directory,
            lazer?.Directory);

    internal static IReadOnlyList<string> BeatmapRoots(
        string localAppData,
        string roaming,
        string? stableDirectory,
        string? lazerDirectory)
    {
        List<string> candidates = [];

        // stable：整个 osu! 目录可以搬到任何盘，Songs 跟着走。
        if (!string.IsNullOrWhiteSpace(stableDirectory))
            candidates.Add(Path.Combine(stableDirectory, "Songs"));
        candidates.Add(Path.Combine(localAppData, "osu!", "Songs"));

        // lazer：默认在 roaming，便携安装由 storage.ini 指到别处。
        candidates.Add(Path.Combine(roaming, "osu", "files"));
        if (!string.IsNullOrWhiteSpace(lazerDirectory))
        {
            foreach (string iniDirectory in new[] { lazerDirectory, Path.Combine(lazerDirectory, "current") })
            {
                string? storage = readStorageIni(iniDirectory);
                if (storage is null)
                    continue;
                candidates.Add(Path.Combine(storage, "files"));
            }
        }

        List<string> roots = [];
        foreach (string candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
                continue;
            if (roots.Any(existing => string.Equals(existing, candidate, StringComparison.OrdinalIgnoreCase)))
                continue;
            roots.Add(Path.GetFullPath(candidate));
        }

        return roots;
    }

    /// <summary>常见安装目录的候选 exe，按可信度排。抽出来便于测试。</summary>
    internal static IReadOnlyList<string> CommonExecutables(
        OsuClientKind kind,
        string localAppData,
        string programFiles,
        string programFilesX86)
    {
        if (kind == OsuClientKind.Lazer)
        {
            return
            [
                Path.Combine(localAppData, "osulazer", "osu!.exe"),
                Path.Combine(localAppData, "osulazer", "current", "osu!.exe"),
                Path.Combine(localAppData, "Programs", "osu!", "osu!.exe"),
                Path.Combine(localAppData, "osu", "osu!.exe")
            ];
        }

        return
        [
            Path.Combine(localAppData, "osu!", "osu!.exe"),
            Path.Combine(programFilesX86, "osu!", "osu!.exe"),
            Path.Combine(programFiles, "osu!", "osu!.exe")
        ];
    }

    // ---- 探测 ----

    private readonly record struct Candidate(OsuClientKind Kind, string Executable, int Priority, string Source);

    /// <summary>所有能确认存在的候选，按可信度排好。</summary>
    private static List<Candidate> collectCandidates(string configured)
    {
        List<Candidate> candidates = [];

        if (configured.Length > 0)
            candidates.Add(new Candidate(InferClient(configured), configured, 100, "设置"));

        foreach (string running in runningOsuExecutables())
            candidates.Add(new Candidate(InferClient(running), running, 90, "正在运行"));

        candidates.AddRange(protocolCandidates());
        candidates.AddRange(uninstallCandidates());

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        foreach (OsuClientKind kind in new[] { OsuClientKind.Lazer, OsuClientKind.Stable })
        {
            int priority = 60;
            foreach (string path in CommonExecutables(kind, localAppData, programFiles, programFilesX86))
                candidates.Add(new Candidate(kind, path, priority--, "常见目录"));
        }

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Executable) && File.Exists(candidate.Executable))
            .Select(candidate => candidate with { Executable = Path.GetFullPath(candidate.Executable) })
            .DistinctBy(candidate => candidate.Executable, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static OsuInstallation? probe(string normalizedClient, string configured)
    {
        List<Candidate> candidates = collectCandidates(configured);
        if (candidates.Count == 0)
            return null;

        Candidate best = candidates
            .OrderByDescending(candidate => matches(normalizedClient, candidate.Kind))
            .ThenByDescending(candidate => candidate.Priority)
            .First();

        AppLog.Info($"osu! 定位：{tag(best.Kind)} → {best.Executable}（来源：{best.Source}；" +
                    $"设置={normalizedClient}，候选 {candidates.Count} 个）");
        return new OsuInstallation(best.Kind, best.Executable, best.Source);

        static bool matches(string setting, OsuClientKind kind) => setting switch
        {
            "lazer" => kind == OsuClientKind.Lazer,
            "stable" => kind == OsuClientKind.Stable,
            _ => true
        };
    }

    /// <summary>某个客户端的候选里挑最好的一个；没有就 null。</summary>
    private static OsuInstallation? pick(List<Candidate> candidates, OsuClientKind kind)
    {
        Candidate? best = candidates
            .OrderByDescending(candidate => candidate.Priority)
            .Cast<Candidate?>()
            .FirstOrDefault();
        if (best is null)
            return null;

        AppLog.Info($"osu! 定位（按客户端扫）：{tag(kind)} → {best.Value.Executable}（来源：{best.Value.Source}）");
        return new OsuInstallation(kind, best.Value.Executable, best.Value.Source);
    }

    private static string tag(OsuClientKind kind) => kind == OsuClientKind.Lazer ? "lazer" : "stable";

    /// <summary>正在运行的 osu! 进程的 exe 路径（32/64 位都读得到就返回，读不到就跳过）。</summary>
    private static IEnumerable<string> runningOsuExecutables()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception exception)
        {
            AppLog.Info($"枚举进程失败，跳过「正在运行」探测：{exception.Message}");
            return [];
        }

        List<string> found = [];
        foreach (Process process in processes)
        {
            try
            {
                if (!process.ProcessName.StartsWith("osu", StringComparison.OrdinalIgnoreCase))
                    continue;

                string? path = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(path) &&
                    Path.GetFileName(path).Equals("osu!.exe", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(path);
                }
            }
            catch
            {
                // 权限不足 / 进程已退出：当作没看见。
            }
            finally
            {
                process.Dispose();
            }
        }

        return found;
    }

    private static IEnumerable<Candidate> protocolCandidates()
    {
        (string Key, OsuClientKind Kind)[] keys =
        [
            (LazerFileProtocol, OsuClientKind.Lazer),
            (LazerProtocol, OsuClientKind.Lazer),
            (StableFileProtocol, OsuClientKind.Stable),
            (StableProtocol, OsuClientKind.Stable),
            (LegacyProtocol, OsuClientKind.Lazer)
        ];

        foreach ((string key, OsuClientKind kind) in keys)
        {
            foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                string? command = readRegistryDefault(root, key);
                string? path = ParseCommandLinePath(command);
                if (path is not null)
                    yield return new Candidate(InferClient(path), path, 80, "注册表协议");
            }
        }
    }

    private static IEnumerable<Candidate> uninstallCandidates()
    {
        const string uninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        RegistryKey[] roots = [Registry.CurrentUser, Registry.LocalMachine];

        foreach (RegistryKey root in roots)
        {
            foreach (string subtree in new[] { uninstallRoot, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            {
                using RegistryKey? uninstall = tryOpen(root, subtree);
                if (uninstall is null)
                    continue;

                foreach (string name in enumerateSubKeys(uninstall))
                {
                    using RegistryKey? entry = tryOpen(uninstall, name);
                    if (entry is null)
                        continue;

                    string displayName = entry.GetValue("DisplayName") as string ?? string.Empty;
                    if (!displayName.Contains("osu!", StringComparison.OrdinalIgnoreCase))
                        continue;

                    OsuClientKind kind = displayName.Contains("lazer", StringComparison.OrdinalIgnoreCase) ||
                                         (entry.GetValue("InstallLocation") as string ?? string.Empty)
                                             .Contains("osulazer", StringComparison.OrdinalIgnoreCase)
                        ? OsuClientKind.Lazer
                        : OsuClientKind.Stable;

                    string? executable = stripIconSuffix(entry.GetValue("DisplayIcon") as string);
                    if (executable is null)
                    {
                        string location = entry.GetValue("InstallLocation") as string ?? string.Empty;
                        if (location.Length > 0)
                            executable = Path.Combine(location, "osu!.exe");
                    }

                    if (executable is not null)
                        yield return new Candidate(kind, executable, 70, "卸载项");
                }
            }
        }
    }

    /// <summary>DisplayIcon 常写成 <c>C:\…\osu!.exe,0</c>，去掉图标序号。</summary>
    private static string? stripIconSuffix(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
            return null;
        string text = icon.Trim().Trim('"');
        int comma = text.LastIndexOf(',');
        if (comma > 2 && int.TryParse(text[(comma + 1)..].Trim(), out _))
            text = text[..comma];
        text = text.Trim().Trim('"');
        return text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? text : null;
    }

    private static string? readStorageIni(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
                return null;
            string path = Path.Combine(directory, "storage.ini");
            if (!File.Exists(path))
                return null;
            string? storage = ParseStorageIniPath(File.ReadAllText(path));
            if (storage is null)
                return null;
            return Path.IsPathRooted(storage) ? storage : Path.Combine(directory, storage);
        }
        catch (Exception exception)
        {
            AppLog.Info($"读取 storage.ini 失败（{directory}）：{exception.Message}");
            return null;
        }
    }

    private static string? readRegistryDefault(RegistryKey root, string path)
    {
        using RegistryKey? key = tryOpen(root, path);
        return key?.GetValue(null) as string;
    }

    private static RegistryKey? tryOpen(RegistryKey root, string path)
    {
        try
        {
            return root.OpenSubKey(path);
        }
        catch (Exception exception)
        {
            AppLog.Info($"读取注册表失败（{path}）：{exception.Message}");
            return null;
        }
    }

    private static IEnumerable<string> enumerateSubKeys(RegistryKey key)
    {
        string[] names;
        try
        {
            names = key.GetSubKeyNames();
        }
        catch
        {
            return [];
        }

        return names;
    }
}
