using System.Diagnostics;
using System.IO;
using System.Net.Http;
using OsuRoam.Models;

namespace OsuRoam.Services;

public sealed class BeatmapDownloadService : IDisposable
{
    /// <summary>需要让用户立刻看到的消息（比如「osu! 启动后秒退，导入没生效」）。</summary>
    public event Action<string>? Notice;

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
            import(item, path, settings);
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

    private void import(RecommendationItem item, string path, AppSettings settings)
    {
        importCore(item, path, settings);
    }

    private void importCore(RecommendationItem item, string path, AppSettings settings)
    {
        item.DownloadState = DownloadState.Importing;

        // 找不到 exe 时**不能**默默退化成「交给系统关联」：`.osz` 没有默认打开方式时
        // 那句 Process.Start 什么都不会发生，用户看到的就是「点了导入没反应」。
        OsuInstallation? installation = OsuLocator.Detect(settings.OsuClient, settings.OsuExecutablePath);
        if (installation is null)
        {
            AppLog.Info("导入失败：没有定位到 osu!（设置里的路径为空或不存在，注册表/进程/常见目录都没命中）");
            throw new InvalidOperationException(Loc.T("download.noosu"));
        }

        // 用 CreateProcess（UseShellExecute=false）而不是 ShellExecute：
        // 现场「手动双击游戏正常、从 Roam 带文件启动 7 秒后崩在 AuthNative.dll」，
        // 而同样的命令行从 cmd 里跑是好的。两者唯一的区别就是走 ShellExecute 还是
        // CreateProcess，所以这里改成和 cmd 一样的那条路。
        Process? started;
        try
        {
            started = startClient(installation, path);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Error($"启动 osu! 失败：{installation.Executable}", exception);
            throw new InvalidOperationException(Loc.F("download.nostart", installation.Executable));
        }

        if (started is null)
            throw new InvalidOperationException(Loc.F("download.nostart", installation.Executable));

        AppLog.Info($"导入：启动 {installation.Executable}（{installation.ClientName}，来源：{installation.Source}），" +
                    $"参数 {path}，PID={started.Id}");
        item.DownloadState = DownloadState.Imported;
        watchLaunch(started, installation, path);
    }

    /// <summary>按「exe + 可选谱面路径」拉起游戏，返回进程句柄（失败抛 Win32Exception）。</summary>
    private static Process? startClient(OsuInstallation installation, string? beatmapPath)
    {
        ProcessStartInfo startInfo = new(installation.Executable)
        {
            UseShellExecute = false,
            // 工作目录跟资源管理器双击时一致（游戏自己目录）。
            WorkingDirectory = Path.GetDirectoryName(installation.Executable) ?? string.Empty
        };
        if (beatmapPath is not null)
            startInfo.ArgumentList.Add(beatmapPath);

        return Process.Start(startInfo);
    }

    /// <summary>游戏进程在不在（按 exe 名字匹配，lazer / stable 都是 <c>osu!</c>）。</summary>
    private static bool isClientRunning(OsuInstallation installation)
    {
        string name = Path.GetFileNameWithoutExtension(installation.Executable);
        try
        {
            return Process.GetProcessesByName(name).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>等游戏把主窗口开出来（再留一点余量让它的导入/认证初始化跑完）。</summary>
    private static async Task<bool> waitForClientReadyAsync(OsuInstallation installation, TimeSpan timeout)
    {
        string name = Path.GetFileNameWithoutExtension(installation.Executable);
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                foreach (Process process in Process.GetProcessesByName(name))
                {
                    if (process.MainWindowHandle == IntPtr.Zero)
                        continue;

                    AppLog.Info($"游戏窗口已出现（PID={process.Id}），再等 2.5 秒让它稳定");
                    await Task.Delay(TimeSpan.FromMilliseconds(2500));
                    return true;
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("等待 osu! 窗口失败", exception);
                return false;
            }

            await Task.Delay(500);
        }

        return false;
    }

    /// <summary>
    /// 退路：先**不带参数**把游戏拉起来，等它初始化完，再把谱面交过去。
    /// 现场那台机器上「冷启动 + 带谱面参数」会让 lazer 在 AuthNative.dll 里崩掉
    /// （7 秒左右，0xC0000409），而游戏已经在跑时把文件交过去是好的 —— 也就是
    /// 正常漫游时走的那条路。所以退路就是先造出「游戏已经在跑」这个前提。
    /// </summary>
    private async Task warmRetryAsync(OsuInstallation installation, string path)
    {
        try
        {
            Notice?.Invoke(Loc.T("import.retrywarm"));
            AppLog.Info("导入退路：先不带参数启动游戏，就绪后再传入谱面");

            if (!isClientRunning(installation))
            {
                startClient(installation, null);
                if (!await waitForClientReadyAsync(installation, TimeSpan.FromSeconds(30)))
                {
                    string message = Loc.T("import.retrytimeout");
                    AppLog.Error(message);
                    Notice?.Invoke(message);
                    return;
                }
            }

            Process? handoff = startClient(installation, path);
            AppLog.Info($"导入退路：把谱面交给已在运行的游戏，PID={handoff?.Id}");
            Notice?.Invoke(Loc.T("import.retryok"));
        }
        catch (Exception exception)
        {
            AppLog.Error("导入退路失败", exception);
            Notice?.Invoke(Loc.T("import.retrytimeout"));
        }
    }

    /// <summary>
    /// 盯着刚拉起来的 osu! 进程一小会儿：几十秒内非正常退出就说明「导入没生效」，
    /// 而且原因在游戏那边（比如崩溃），不是 Roam 没触发 —— 以前这种情况界面上一片安静，
    /// 只看到「tosu 已连接」但什么都不会发生。
    /// </summary>
    private void watchLaunch(Process process, OsuInstallation installation, string path)
    {
        try
        {
            DateTime startedAt = DateTime.UtcNow;
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                double seconds = (DateTime.UtcNow - startedAt).TotalSeconds;
                int code;
                try { code = process.ExitCode; } catch { return; }

                // 唯一正常的情况：游戏本来就在跑，新进程把文件交过去后立即退出（码 0）。
                if (code == 0 || seconds > 30)
                    return;

                string message = Loc.F("import.gamequit", seconds.ToString("0.0"), $"0x{code:X8}");
                AppLog.Error(message);
                // 游戏是我们拉起来的，会继承当前进程的环境。崩的时候把可疑的几项记下来，
                // 免得下次还要靠猜（现场：手动双击游戏正常、从 Roam 拉起就崩）。
                AppLog.Info($"导入环境：TMP={Environment.GetEnvironmentVariable("TMP")}｜" +
                            $"TEMP={Environment.GetEnvironmentVariable("TEMP")}｜" +
                            $"USERPROFILE={Environment.GetEnvironmentVariable("USERPROFILE")}｜" +
                            $"cwd={Environment.CurrentDirectory}");
                Notice?.Invoke(message);
                _ = warmRetryAsync(installation, path);
            };
        }
        catch (Exception exception)
        {
            AppLog.Error("监听 osu! 启动进程失败", exception);
        }
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

    public void Dispose()
    {
        http.Dispose();
        queue.Dispose();
        importGate.Dispose();
    }
}
