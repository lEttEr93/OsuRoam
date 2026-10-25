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
    private readonly SemaphoreSlim queue = new(1, 1);
    private readonly string cacheDirectory;

    public BeatmapDownloadService(string cacheDirectory)
    {
        this.cacheDirectory = cacheDirectory;
    }

    public async Task DownloadAndImportAsync(RecommendationItem item, AppSettings settings, CancellationToken cancellationToken)
    {
        await DownloadAsync(item, settings, cancellationToken);
        if (settings.AutoImport)
            await ImportAsync(item, settings, cancellationToken);
    }

    public async Task DownloadAsync(RecommendationItem item, AppSettings settings, CancellationToken cancellationToken)
    {
        await queue.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            string path = Path.Combine(cacheDirectory, $"{item.BeatmapsetId}.osz");
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
        await queue.WaitAsync(cancellationToken);
        try
        {
            string path = Path.Combine(cacheDirectory, $"{item.BeatmapsetId}.osz");
            if (!File.Exists(path) || new FileInfo(path).Length < 1024)
                throw new FileNotFoundException("预下载的谱面文件不存在。", path);
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
            queue.Release();
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
            throw new InvalidOperationException("下载地址模板必须包含 {setId}。");

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

        throw new HttpRequestException($"所有下载源均失败。最后错误：{lastException?.Message}", lastException);
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
                throw new InvalidDataException("下载内容为空。");
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
            throw new InvalidDataException("镜像返回的不是有效 OSZ 文件。");
    }

    private static void import(RecommendationItem item, string path, string configuredExecutable, string osuClient)
    {
#if false
        item.DownloadState = DownloadState.Importing;
        string? executable = resolveOsuExecutable(configuredExecutable);
        ProcessStartInfo startInfo = executable is null
            ? new ProcessStartInfo(path) { UseShellExecute = true }
            : new ProcessStartInfo(executable, $"\"{path}\"") { UseShellExecute = true };
        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 osu!lazer 导入谱面。");
        item.DownloadState = DownloadState.Imported;
#endif
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
            throw new InvalidOperationException("无法启动 osu!lazer 导入谱面。");
        item.DownloadState = DownloadState.Imported;
    }

    private static string? resolveOsuExecutable(string configured, string osuClient)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!File.Exists(configured))
                throw new FileNotFoundException("设置中的 osu!.exe 路径不存在。", configured);
            return configured;
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
    }
}
