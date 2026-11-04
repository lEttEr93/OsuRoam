using System.Diagnostics;
using System.IO;
using System.Net.Http;
using OsuRoam.Models;

namespace OsuRoam.Services;

public sealed class BeatmapDownloadService : IDisposable
{
    private readonly HttpClient http = new(new SocketsHttpHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 2
    })
    {
        Timeout = TimeSpan.FromMinutes(10),
        DefaultRequestVersion = new Version(1, 1),
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
    };
    /// <summary>
    /// 下载并发数。单镜像实测 12–28 MB 要 37–87 秒，一路串行时池子里 5 首等于几分钟，
    /// 期间界面上的「预下载」会看起来整体卡住；两路能在同带宽下把等待时间砍半，
    /// 又不会把镜像限流踩爆（失败会走 fallback 镜像）。
    /// </summary>
    public const int MaxConcurrentDownloads = 2;

    private readonly SemaphoreSlim queue = new(MaxConcurrentDownloads, MaxConcurrentDownloads);
    /// <summary>导入单独一把锁：只串行化「读缓存 + 启动 osu!」，不跟下载抢。</summary>
    private readonly SemaphoreSlim importGate = new(1, 1);
    private readonly string cacheDirectory;

    public BeatmapDownloadService(string cacheDirectory)
    {
        this.cacheDirectory = cacheDirectory;
        // catboy.best 这类国际镜像对空 User-Agent 直接回 403，必须带上身份再请求。
        string version = typeof(BeatmapDownloadService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"osu-roam/{version}");
    }

    public async Task DownloadAndImportAsync(RecommendationItem item, AppSettings settings, CancellationToken cancellationToken)
    {
        await DownloadAsync(item, settings, cancellationToken);
        if (settings.AutoImport)
            await ImportAsync(item, settings, cancellationToken);
    }

    public async Task DownloadAsync(RecommendationItem item, AppSettings settings, CancellationToken cancellationToken)
    {
        string path = Path.Combine(cacheDirectory, $"{item.BeatmapsetId}.osz");

        // 已经在缓存里就别排队了。预下载正占着 queue 的时候，导入下一首会在这里
        // 白等一整首谱面的下载（1.5.0 的「导入总要等新那张下完」就是卡在这）。
        if (File.Exists(path) && new FileInfo(path).Length >= 1024)
        {
            item.DownloadProgress = 1;
            item.DownloadState = DownloadState.Downloaded;
            return;
        }

        await queue.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            if (!File.Exists(path) || new FileInfo(path).Length < 1024)
                await downloadWithFallbackAsync(item, settings, path, cancellationToken);
            else
            {
                item.DownloadProgress = 1;
                item.DownloadState = DownloadState.Downloaded;
            }
        }
        catch (OperationCanceledException)
        {
            item.DownloadState = DownloadState.Waiting;
            // 取消（卡片被换掉 / 用户停手）时别把半截文件留在缓存里，否则下次会当成已下载。
            try
            {
                if (File.Exists(path + ".part"))
                    File.Delete(path + ".part");
            }
            catch
            {
                // 删不掉不影响主流程
            }
            throw;
        }
        catch
        {
            item.DownloadState = DownloadState.Failed;
            throw;
        }
        finally
        {
            queue.Release();
        }
    }

    public async Task ImportAsync(RecommendationItem item, AppSettings settings, CancellationToken cancellationToken)
    {
        // 导入走自己的闸门：不能排在下载后面。导入只是校验 + Process.Start（几十毫秒），
        // 而一次下载要好几秒——预下载占着 queue 时，用户会眼看着「下一首」迟迟不动。
        await importGate.WaitAsync(cancellationToken);
        try
        {
            string path = Path.Combine(cacheDirectory, $"{item.BeatmapsetId}.osz");
            if (!File.Exists(path) || new FileInfo(path).Length < 1024)
                throw new FileNotFoundException(Loc.T("download.missing"), path);
            await validateOszAsync(path, cancellationToken);
            import(item, path, settings.OsuExecutablePath, settings.OsuClient);
            // osu! receives the path asynchronously. Keep the file in place after
            // Process.Start so lazer can finish reading the archive before cleanup.
            AppLog.Info($"已触发导入，保留缓存等待 osu! 读取：{item.DisplayTitle} ({item.BeatmapsetId})");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            item.DownloadState = DownloadState.Failed;
            throw;
        }
        finally
        {
            importGate.Release();
        }
    }

    private async Task downloadWithFallbackAsync(RecommendationItem item, AppSettings settings, string finalPath, CancellationToken cancellationToken)
    {
        string templates = string.IsNullOrWhiteSpace(settings.DownloadMirrorTemplates)
            ? settings.DownloadUrlTemplate
            : settings.DownloadMirrorTemplates;
        string[] urls = templates.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(template => template.Contains("{setId}", StringComparison.Ordinal))
            .Select(template => template.Replace("{setId}", item.BeatmapsetId.ToString(), StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (urls.Length == 0)
            throw new InvalidOperationException(Loc.T("download.badtemplate"));

        Exception? lastException = null;
        foreach (string url in urls)
        {
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    await downloadAsync(item, url, finalPath, cancellationToken);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    lastException = exception;
                    AppLog.Error($"下载源失败（第 {attempt} 次）：{url}", exception);
                    if (attempt < 2)
                    {
                        try { await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken); }
                        catch (OperationCanceledException) { throw; }
                    }
                }
            }
        }

        throw new HttpRequestException(Loc.F("download.allfailed", lastException?.Message), lastException);
    }

    private async Task downloadAsync(RecommendationItem item, string url, string finalPath, CancellationToken cancellationToken)
    {
        string partialPath = finalPath + ".part";
        item.DownloadState = DownloadState.Downloading;
        item.DownloadProgress = 0;
        try
        {
            using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            long received = 0;
            await using (Stream input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (FileStream output = new(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true))
            {
                byte[] buffer = new byte[64 * 1024];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    item.DownloadProgress = total > 0 ? (double)received / total.Value : 0;
                }
                await output.FlushAsync(cancellationToken);
            }

            if (received < 4)
                throw new InvalidDataException(Loc.T("download.empty"));
            await validateOszAsync(partialPath, cancellationToken);
            File.Move(partialPath, finalPath, true);
            AppLog.Info($"下载完成：{url} -> {finalPath} ({received} bytes)");
            item.DownloadProgress = 1;
            item.DownloadState = DownloadState.Downloaded;
        }
        finally
        {
            if (File.Exists(partialPath))
                File.Delete(partialPath);
        }
    }

    private static async Task validateOszAsync(string path, CancellationToken cancellationToken)
    {
        byte[] signature = new byte[4];
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        int read = await stream.ReadAsync(signature, cancellationToken);
        if (read != 4 || signature[0] != (byte)'P' || signature[1] != (byte)'K')
            throw new InvalidDataException(Loc.T("download.badosz"));
    }

    private static void import(RecommendationItem item, string path, string configuredExecutable, string osuClient)
    {
        importCore(item, path, configuredExecutable, osuClient);
    }

    private static void importCore(RecommendationItem item, string path, string configuredExecutable, string osuClient)
    {
        item.DownloadState = DownloadState.Importing;
        string? executable = resolveOsuExecutable(configuredExecutable, osuClient);
        ProcessStartInfo startInfo;
        if (executable is null)
        {
            startInfo = new ProcessStartInfo(path) { UseShellExecute = true };
        }
        else
        {
            startInfo = new ProcessStartInfo(executable) { UseShellExecute = true };
            startInfo.ArgumentList.Add(path);
        }
        Process? started = Process.Start(startInfo);
        if (started is null)
            throw new InvalidOperationException(Loc.T("download.nostart"));
        AppLog.Info(executable is null
            ? $"导入：没有找到 osu 可执行文件，交给系统关联打开 {Path.GetFileName(path)}"
            : $"导入：启动 {executable}，参数 {path}，PID={started.Id}");
        item.DownloadState = DownloadState.Imported;
    }

    /// <summary>
    /// 清理「复制文件地址」带进来的引号、首尾空白和 %VAR%，否则 File.Exists 永远失败，
    /// 表现为每次导入都报「设置中的 osu!.exe 路径不存在」。
    /// </summary>
    internal static string NormalizeExecutablePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string value = raw.Trim().Trim('"', '\'', ' ');
        if (value.Length == 0)
            return string.Empty;

        try
        {
            value = Environment.ExpandEnvironmentVariables(value);
        }
        catch (ArgumentException)
        {
            // 环境变量写法不合法时保留原文，交给后面的存在性判断。
        }

        return value.Trim().Trim('"', '\'', ' ');
    }

    private static string? resolveOsuExecutable(string configured, string osuClient)
    {
        string normalized = NormalizeExecutablePath(configured);
        if (normalized.Length > 0)
        {
            if (File.Exists(normalized))
                return normalized;

            // 不能直接抛：配置里可能是旧版本写进去的脏路径，或者用户挪了安装目录。
            // 抛异常会让之后每一次导入都失败，回退到自动探测更安全。
            AppLog.Info($"设置里的 osu! 路径不可用，改用自动探测：{normalized}");
        }

        return GetOsuExecutableCandidates(
                osuClient,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
            .FirstOrDefault(File.Exists);
    }

    internal static IReadOnlyList<string> GetOsuExecutableCandidates(
        string osuClient,
        string localApplicationData,
        string programFilesX86,
        string programFiles)
    {
        if (string.Equals(osuClient, "stable", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                Path.Combine(localApplicationData, "osu!", "osu!.exe"),
                Path.Combine(programFilesX86, "osu!", "osu!.exe"),
                Path.Combine(programFiles, "osu!", "osu!.exe")
            ];
        }

        return
        [
            Path.Combine(localApplicationData, "osulazer", "osu!.exe"),
            Path.Combine(localApplicationData, "Programs", "osu!", "osu!.exe")
        ];
    }

    public void Dispose()
    {
        http.Dispose();
        queue.Dispose();
        importGate.Dispose();
    }
}
