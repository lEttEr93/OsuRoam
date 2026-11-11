using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OsuRoam.Services;

/// <summary>
/// 检查更新 + 就地更新。
///
/// 为什么可以就地更新：便携包是「exe + dll + Assets + Fonts + tosu.exe」平铺在一个目录里，
/// 而 `data\`（配置 / 谱面缓存 / 日志）是独立子目录。所以更新 = 把新包解压后盖上去，
/// 不碰 `data\`。运行中的 exe 不能被覆盖，因此写一个 cmd 脚本、让它在 Roam 退出后
/// 再 robocopy，最后重新启动新版本 —— 用户不用再去下载便携包。
///
/// 版本号直接拿 release 的 tag（`v1.5.6.2`）和本机的 InformationalVersion 比，
/// 不能只看 <c>releases/latest</c>：这个仓库里还发过 `sv-v0.1.0` 这种另一个产品的 release。
/// </summary>
public static class UpdateService
{
    /// <summary>发版用的仓库（公开仓库；私人仓库匿名读不到）。</summary>
    public const string Repository = "lEttEr93/OsuRoam";

    private const string ReleasesApi = $"https://api.github.com/repos/{Repository}/releases?per_page=30";

    /// <summary>更新包的临时目录（在 data\ 里，不会被就地更新覆盖掉）。</summary>
    public static string WorkingDirectory => Path.Combine(StoragePaths.Root, "update");

    // ---- 版本比较 ----

    /// <summary>
    /// tag / 版本号的可比较形式：数字段 + 后缀。
    /// 1.5.6.2 &gt; 1.5.6fix &gt; 1.5.6 &gt; 1.5.3-beta2 &gt; 1.5.3。
    /// </summary>
    public readonly struct UpdateVersion : IComparable<UpdateVersion>, IEquatable<UpdateVersion>
    {
        private readonly int[] numbers;
        private readonly string suffix;

        private UpdateVersion(int[] numbers, string suffix)
        {
            this.numbers = numbers;
            this.suffix = suffix;
        }

        public int[] Numbers => numbers ?? [];

        /// <summary>数字段后面的东西（fix / beta2 / …），小写、去掉分隔符。</summary>
        public string Suffix => suffix ?? string.Empty;

        public static bool TryParse(string? text, out UpdateVersion version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string value = text.Trim();
            if (value.StartsWith('v') || value.StartsWith('V'))
                value = value[1..];

            int index = 0;
            while (index < value.Length && (char.IsAsciiDigit(value[index]) || value[index] == '.'))
                index++;

            string numeric = value[..index].Trim('.');
            if (numeric.Length == 0)
                return false;

            List<int> parts = [];
            foreach (string part in numeric.Split('.'))
            {
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    return false;
                parts.Add(number);
            }
            if (parts.Count == 0)
                return false;

            string rest = value[index..].Trim();
            version = new UpdateVersion([.. parts], rest.ToLowerInvariant());
            return true;
        }

        public static UpdateVersion Parse(string text) =>
            TryParse(text, out UpdateVersion version) ? version : throw new FormatException($"不是版本号：{text}");

        /// <summary>后缀的先后：预发布 &lt; 正式 &lt; 修补（fix / patch）。</summary>
        private static int suffixRank(string suffix)
        {
            if (suffix.Length == 0)
                return 1;
            if (suffix.Contains("beta", StringComparison.Ordinal) ||
                suffix.Contains("alpha", StringComparison.Ordinal) ||
                suffix.Contains("pre", StringComparison.Ordinal) ||
                suffix.Contains("rc", StringComparison.Ordinal))
                return 0;
            return 2;
        }

        public int CompareTo(UpdateVersion other)
        {
            int[] left = Numbers;
            int[] right = other.Numbers;
            int length = Math.Max(left.Length, right.Length);
            for (int index = 0; index < length; index++)
            {
                int a = index < left.Length ? left[index] : 0;
                int b = index < right.Length ? right[index] : 0;
                if (a != b)
                    return a < b ? -1 : 1;
            }

            int rank = suffixRank(Suffix).CompareTo(suffixRank(other.Suffix));
            return rank != 0 ? rank : string.CompareOrdinal(Suffix, other.Suffix);
        }

        public bool Equals(UpdateVersion other) => CompareTo(other) == 0;
        public override bool Equals(object? obj) => obj is UpdateVersion other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Numbers.Length, Suffix);
        public override string ToString() => $"{string.Join('.', Numbers)}{Suffix}";

        public static bool operator >(UpdateVersion left, UpdateVersion right) => left.CompareTo(right) > 0;
        public static bool operator <(UpdateVersion left, UpdateVersion right) => left.CompareTo(right) < 0;
        public static bool operator ==(UpdateVersion left, UpdateVersion right) => left.CompareTo(right) == 0;
        public static bool operator !=(UpdateVersion left, UpdateVersion right) => left.CompareTo(right) != 0;
    }

    // ---- 发布信息 ----

    public sealed record UpdateAsset(string Name, Uri DownloadUri, long Size, string? Sha256);

    public sealed record UpdateRelease(string Tag, UpdateVersion Version, DateTimeOffset? PublishedAt, Uri PageUri, UpdateAsset Asset);

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("browser_download_url")] public string? DownloadUrl { get; set; }
        [JsonPropertyName("digest")] public string? Digest { get; set; }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? Tag { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
    }

    /// <summary>便携包的文件名：中英两版都是 <c>osu-roam-&lt;版本&gt;-portable.zip</c>。</summary>
    private static bool isPortableAsset(string name) =>
        name.StartsWith("osu-roam-", StringComparison.OrdinalIgnoreCase) &&
        name.EndsWith("-portable.zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>英文版的包名里带 <c>-en-portable</c>。</summary>
    private static bool isEnglishAsset(string name) =>
        name.Contains("-en-portable", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 从 releases JSON 里挑出「当前语言版本该更新的那个 release」。
    /// 不认识的东西（比如 sv-v0.1.0 那个别的产品的 release）会被自然排除 —— 它没有便携包资源。
    /// </summary>
    public static UpdateRelease? ParseReleases(string json, bool english)
    {
        List<GitHubRelease>? releases;
        try
        {
            releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
        if (releases is null)
            return null;

        UpdateRelease? best = null;
        foreach (GitHubRelease release in releases)
        {
            if (release.Draft || release.Prerelease || string.IsNullOrWhiteSpace(release.Tag))
                continue;
            if (!UpdateVersion.TryParse(release.Tag, out UpdateVersion version))
                continue;

            UpdateAsset? asset = pickAsset(release.Assets, english);
            if (asset is null)
                continue;

            Uri page = Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out Uri? parsedPage)
                ? parsedPage
                : new Uri($"https://github.com/{Repository}/releases/tag/{release.Tag}", UriKind.Absolute);

            UpdateRelease candidate = new(release.Tag, version, release.PublishedAt, page, asset);
            if (best is null || candidate.Version > best.Version ||
                (candidate.Version.Equals(best.Version) && candidate.PublishedAt > best.PublishedAt))
                best = candidate;
        }

        return best;
    }

    private static UpdateAsset? pickAsset(List<GitHubAsset>? assets, bool english)
    {
        if (assets is null || assets.Count == 0)
            return null;

        // 先找语言对得上的包；没有就退回另一种（总比没有好，界面语言存在配置里不会丢）。
        List<GitHubAsset> portable = [.. assets.Where(asset =>
            asset.Name is { } name && isPortableAsset(name) &&
            Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out _))];

        GitHubAsset? picked = portable.FirstOrDefault(asset => isEnglishAsset(asset.Name!) == english)
            ?? portable.FirstOrDefault();
        if (picked is null)
            return null;

        string? sha256 = null;
        if (picked.Digest is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            sha256 = digest["sha256:".Length..].Trim().ToLowerInvariant();

        return new UpdateAsset(picked.Name!, new Uri(picked.DownloadUrl!, UriKind.Absolute), picked.Size, sha256);
    }

    // ---- 检查 ----

    private static HttpClient createClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(AppVersion.UserAgent);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>
    /// 查最新版本。返回 null = 已经是最新；抛异常 = 查不到（网络 / 限流 / 没有可用的包）。
    /// </summary>
    public static async Task<UpdateRelease?> CheckAsync(bool english, CancellationToken token)
    {
        using HttpClient client = createClient();
        using HttpResponseMessage response = await client.GetAsync(ReleasesApi, token).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden ||
            response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("GitHub 接口限流了（每小时 60 次），过一会儿再试");

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub 返回 {(int)response.StatusCode} {response.ReasonPhrase}");

        string json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        UpdateRelease? release = ParseReleases(json, english)
            ?? throw new InvalidOperationException("没找到可用的发行包");

        UpdateVersion current = UpdateVersion.Parse(AppVersion.Display);
        return release.Version > current ? release : null;
    }

    // ---- 下载 ----

    /// <summary>下载更新包，返回本地路径。带 SHA-256 校验（release 里给了的话）。</summary>
    public static async Task<string> DownloadAsync(
        UpdateAsset asset,
        IProgress<long>? progress,
        CancellationToken token)
    {
        Directory.CreateDirectory(WorkingDirectory);
        string path = Path.Combine(WorkingDirectory, asset.Name);

        using (HttpClient client = createClient())
        using (HttpResponseMessage response = await client
                   .GetAsync(asset.DownloadUri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"下载失败：{(int)response.StatusCode} {response.ReasonPhrase}");

            await using Stream source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using FileStream target = new(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            byte[] buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                int read = await source.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read <= 0)
                    break;
                await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                total += read;
                progress?.Report(total);
            }
        }

        long length = new FileInfo(path).Length;
        if (asset.Size > 0 && length != asset.Size)
            throw new InvalidOperationException($"更新包大小不对：应为 {asset.Size} 字节，实际 {length} 字节");

        if (asset.Sha256 is { Length: 64 } expected)
        {
            string actual = await sha256Async(path, token).ConfigureAwait(false);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("更新包校验失败（SHA-256 不匹配），请稍后重试");
        }

        return path;
    }

    private static async Task<string> sha256Async(string path, CancellationToken token)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, token).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ---- 解包 + 应用 ----

    public sealed record UpdatePlan(string Script, string StagedRoot, string InstallRoot, string WorkingDirectory);

    /// <summary>程序目录可写吗（不可写就只能在便携模式下就地更新失败，得让用户手动换）。</summary>
    public static bool InstallDirectoryWritable(string installRoot)
    {
        try
        {
            string probe = Path.Combine(installRoot, $".roam-write-{Guid.NewGuid():N}.tmp");
            using (FileStream stream = File.Create(probe, 1, FileOptions.DeleteOnClose))
                stream.WriteByte(0);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 解压到暂存区、写更新脚本。zip 里是平铺的（没有顶层目录），
    /// 但万一以后带了顶层目录，这里也能找到 OsuRoam.exe 所在的那一层。
    /// </summary>
    public static UpdatePlan Prepare(string zipPath, string installRoot, string? workingDirectory = null)
    {
        string work = workingDirectory ?? WorkingDirectory;
        Directory.CreateDirectory(work);
        string staged = Path.Combine(work, "staged");
        if (Directory.Exists(staged))
            Directory.Delete(staged, true);

        ZipFile.ExtractToDirectory(zipPath, staged, overwriteFiles: true);

        string root = staged;
        if (!File.Exists(Path.Combine(root, "OsuRoam.exe")))
        {
            foreach (string directory in Directory.EnumerateDirectories(staged))
            {
                if (File.Exists(Path.Combine(directory, "OsuRoam.exe")))
                {
                    root = directory;
                    break;
                }
            }
        }

        if (!File.Exists(Path.Combine(root, "OsuRoam.exe")))
            throw new InvalidOperationException("更新包不完整：里面没有 OsuRoam.exe");

        string script = Path.Combine(work, "apply-update.cmd");
        File.WriteAllBytes(script, writeScript());

        return new UpdatePlan(script, root, installRoot, work);
    }

    /// <summary>
    /// 更新脚本。**必须保持 ASCII + CRLF**：cmd.exe 对 LF 换行和中文都不友好。
    /// 逻辑：等旧进程退出 → robocopy 覆盖（不删多余文件，所以 data\ 原样保留）→ 启动新版本 → 清理。
    ///
    /// 参数走环境变量而不是命令行：路径里可能有空格，`cmd /c ""a b"" 1 "c d""` 这种嵌套引号
    /// 极易出错，环境变量没有这个问题。
    /// 也不用 timeout 做等待：脚本是无窗口启动的，没有控制台，timeout 会直接报错退出。
    /// </summary>
    private static byte[] writeScript()
    {
        string text =
            "@echo off\r\n" +
            "setlocal\r\n" +
            "set \"PID=%ROAM_UPDATE_PID%\"\r\n" +
            "set \"STAGE=%ROAM_UPDATE_STAGE%\"\r\n" +
            "set \"TARGET=%ROAM_UPDATE_TARGET%\"\r\n" +
            "set \"WORK=%ROAM_UPDATE_WORK%\"\r\n" +
            "set \"RESULT=%WORK%\\last-result.txt\"\r\n" +
            "rem wait for the running instance to exit (max ~120s)\r\n" +
            "for /l %%i in (1,1,120) do (\r\n" +
            "    tasklist /FI \"PID eq %PID%\" 2>nul | find \"%PID%\" >nul || goto install\r\n" +
            "    ping -n 2 127.0.0.1 >nul\r\n" +
            ")\r\n" +
            ":install\r\n" +
            "rem no /purge and no /mir: files that are not in the package stay as they are\r\n" +
            "robocopy \"%STAGE%\" \"%TARGET%\" /e /nfl /ndl /njh /njs /np >nul\r\n" +
            "if errorlevel 8 goto fail\r\n" +
            ">\"%RESULT%\" echo ok\r\n" +
            "goto restart\r\n" +
            ":fail\r\n" +
            ">\"%RESULT%\" echo failed %errorlevel%\r\n" +
            ":restart\r\n" +
            "rem always restart, even after a failed copy: the old exe is still there\r\n" +
            "start \"\" \"%TARGET%\\OsuRoam.exe\"\r\n" +
            "rmdir /s /q \"%STAGE%\" 2>nul\r\n" +
            "del /q \"%WORK%\\*.zip\" 2>nul\r\n" +
            "start \"\" /b cmd /c \"ping -n 3 127.0.0.1 >nul & del /q \"\"%~f0\"\"\"\r\n" +
            "exit /b 0\r\n";

        return Encoding.ASCII.GetBytes(text);
    }

    /// <summary>启动更新脚本。调用方随后就该退出程序。</summary>
    public static void Launch(UpdatePlan plan, int processId)
    {
        ProcessStartInfo info = new("cmd.exe", "/c apply-update.cmd")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = plan.WorkingDirectory
        };
        info.Environment["ROAM_UPDATE_PID"] = processId.ToString(CultureInfo.InvariantCulture);
        info.Environment["ROAM_UPDATE_STAGE"] = plan.StagedRoot;
        info.Environment["ROAM_UPDATE_TARGET"] = plan.InstallRoot;
        info.Environment["ROAM_UPDATE_WORK"] = plan.WorkingDirectory;

        Process.Start(info);
        AppLog.Info($"更新脚本已启动：{plan.Script}（旧进程 {processId}，目标 {plan.InstallRoot}）");
    }

    /// <summary>上一次更新脚本留下的结果（ok / failed N）；没有就返回 null。</summary>
    public static string? ReadLastResult()
    {
        try
        {
            string path = Path.Combine(WorkingDirectory, "last-result.txt");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>读了就删，免得每次都提示。</summary>
    public static void ClearLastResult()
    {
        try
        {
            string path = Path.Combine(WorkingDirectory, "last-result.txt");
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception)
        {
            // 删不掉也不影响，下次启动再提示一次而已。
        }
    }


    /// <summary>启动时清掉上次留下的暂存区（下载的 zip 留着，失败时能用它手动换）。</summary>
    public static void CleanupStaging()
    {
        try
        {
            string staged = Path.Combine(WorkingDirectory, "staged");
            if (Directory.Exists(staged))
                Directory.Delete(staged, true);
        }
        catch (Exception exception)
        {
            AppLog.Info($"清理更新暂存区失败（不影响使用）：{exception.Message}");
        }
    }
}
