using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using OsuRoam.Models;

namespace OsuRoam.Services;

public sealed record TosuStartResult(bool Ready, bool StartedByRoam, string? ExecutablePath);

public sealed class TosuProcessManager
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim startGate = new(1, 1);
    private Process? managedProcess;

    public event Action<string>? StatusChanged;

    /// <summary>
    /// tosu 读不到 osu! 的内存（日志里出现 <c>Couldn't read …</c>）。
    /// 典型是 lazer 比 tosu 新：tosu 按进程找到了游戏，但结构体偏移全对不上，
    /// 于是 WebSocket 一直连得上、数据却是空的 —— 信息栏拿不到谱面。
    /// </summary>
    public bool MemoryReadFailing { get; private set; }

    public async Task<TosuStartResult> EnsureReadyAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        Uri endpoint = parseEndpoint(settings.TosuWebSocketUrl);
        await startGate.WaitAsync(cancellationToken);
        try
        {
            if (await isEndpointReadyAsync(endpoint, cancellationToken))
            {
                bool managed = managedProcess is { HasExited: false };
                StatusChanged?.Invoke(managed ? Loc.T("tosu.ready") : Loc.T("tosu.alreadyrunning"));
                return new TosuStartResult(true, managed, managedProcess?.StartInfo.FileName);
            }

            if (!settings.AutoStartTosu)
            {
                StatusChanged?.Invoke(Loc.T("tosu.manual"));
                return new TosuStartResult(false, false, null);
            }

            if (managedProcess is { HasExited: false })
                return await waitUntilReadyAsync(endpoint, managedProcess, cancellationToken);

            string? executablePath = ResolveExecutablePath(
                settings.TosuExecutablePath,
                AppContext.BaseDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.CurrentDirectory);
            if (executablePath is null)
                throw new FileNotFoundException(Loc.T("tosu.missing"));

            await disableDashboardAsync(executablePath, cancellationToken);
            StatusChanged?.Invoke(Loc.T("tosu.starting"));
            AppLog.Info($"正在启动 tosu：{executablePath}");
            MemoryReadFailing = false;

            Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    WorkingDirectory = Path.GetDirectoryName(executablePath)!,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            // tosu 是 pkg 打的 Node 单文件：启动时要把原生模块解到
            // %USERPROFILE%\.cache\pkg\<hash>，用户目录权限一有问题就
            // 「EPERM: mkdir …」然后退出码 1（现场就是这台机器）。
            // 把它的用户目录指到 Roam 自己的 data 下，这条路不依赖配置文件权限。
            string tosuHome = Path.Combine(StoragePaths.Root, "tosu-home");
            try
            {
                Directory.CreateDirectory(tosuHome);
                process.StartInfo.Environment["USERPROFILE"] = tosuHome;
                process.StartInfo.Environment["HOME"] = tosuHome;
                AppLog.Info($"tosu 的用户目录：{tosuHome}");
            }
            catch (Exception exception)
            {
                AppLog.Error("tosu 的临时目录不可用，退回默认用户目录", exception);
            }

            process.StartInfo.ArgumentList.Add("--update=false");
            process.OutputDataReceived += (_, args) => logProcessLine("tosu", args.Data);
            process.ErrorDataReceived += (_, args) => logProcessLine("tosu stderr", args.Data);
            process.Exited += (_, _) =>
            {
                int? exitCode = null;
                try { exitCode = process.ExitCode; } catch { }
                AppLog.Info($"由 Roam 启动的 tosu 已退出{(exitCode.HasValue ? $"，退出码 {exitCode}" : string.Empty)}");
                StatusChanged?.Invoke(Loc.T("tosu.exited"));
            };

            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException(Loc.T("tosu.startfail"));
            }

            managedProcess = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return await waitUntilReadyAsync(endpoint, process, cancellationToken);
        }
        finally
        {
            startGate.Release();
        }
    }

    public async Task ShutdownAsync(bool stopManagedProcess)
    {
        await startGate.WaitAsync();
        try
        {
            Process? process = managedProcess;
            managedProcess = null;
            if (process is null)
                return;

            if (stopManagedProcess && !process.HasExited)
            {
                AppLog.Info($"正在关闭由 Roam 启动的 tosu，PID={process.Id}");
                try
                {
                    process.Kill(entireProcessTree: true);
                    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    AppLog.Info(Loc.T("tosu.starttimeout"));
                }
                catch (Exception exception)
                {
                    AppLog.Error("关闭由 Roam 启动的 tosu 失败", exception);
                }
            }
            else if (!stopManagedProcess && !process.HasExited)
            {
                AppLog.Info($"保留由 Roam 启动的 tosu，PID={process.Id}");
            }

            process.Dispose();
        }
        finally
        {
            startGate.Release();
        }
    }

    internal static string? ResolveExecutablePath(string configuredPath, params string[] searchDirectories)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            string fullConfiguredPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredPath.Trim()));
            if (File.Exists(fullConfiguredPath))
                return fullConfiguredPath;
        }

        foreach (string directory in searchDirectories.Where(directory => !string.IsNullOrWhiteSpace(directory)))
        {
            string candidate = Path.Combine(directory, "tosu.exe");
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }
        return null;
    }

    internal static string BuildDashboardDisabledConfig(string existingContent)
    {
        const string setting = "OPEN_DASHBOARD_ON_STARTUP=false";
        if (Regex.IsMatch(existingContent, @"(?m)^\s*OPEN_DASHBOARD_ON_STARTUP\s*="))
            return Regex.Replace(existingContent, @"(?m)^\s*OPEN_DASHBOARD_ON_STARTUP\s*=.*$", setting);

        if (existingContent.Length == 0)
            return setting + Environment.NewLine;
        string separator = existingContent.EndsWith('\n') || existingContent.EndsWith('\r') ? string.Empty : Environment.NewLine;
        return existingContent + separator + setting + Environment.NewLine;
    }

    private static Uri parseEndpoint(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? endpoint) ||
            endpoint.Scheme is not ("ws" or "wss"))
            throw new InvalidOperationException(Loc.T("tosu.badurl"));
        return endpoint;
    }

    private static async Task disableDashboardAsync(string executablePath, CancellationToken cancellationToken)
    {
        string configPath = Path.Combine(Path.GetDirectoryName(executablePath)!, "tosu.env");
        string existing = File.Exists(configPath)
            ? await File.ReadAllTextAsync(configPath, cancellationToken)
            : string.Empty;
        string updated = BuildDashboardDisabledConfig(existing);
        if (!string.Equals(existing, updated, StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(configPath, updated, new UTF8Encoding(false), cancellationToken);
            AppLog.Info($"已关闭 tosu 的启动 Dashboard：{configPath}");
        }
    }

    private async Task<TosuStartResult> waitUntilReadyAsync(Uri endpoint, Process process, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + StartupTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
                throw new InvalidOperationException(Loc.F("tosu.earlyexit", process.ExitCode));
            if (await isEndpointReadyAsync(endpoint, cancellationToken))
            {
                StatusChanged?.Invoke(Loc.T("tosu.ready"));
                AppLog.Info($"tosu WebSocket 已就绪，PID={process.Id}");
                return new TosuStartResult(true, true, process.StartInfo.FileName);
            }
            await Task.Delay(300, cancellationToken);
        }
        throw new TimeoutException(Loc.F("tosu.nolisten", StartupTimeout.TotalSeconds.ToString("0"), endpoint.Host, endpoint.Port));
    }

    private static async Task<bool> isEndpointReadyAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        using ClientWebSocket client = new();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(800));
        try
        {
            await client.ConnectAsync(endpoint, timeout.Token);
            return client.State == WebSocketState.Open;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (WebSocketException)
        {
            return false;
        }
    }

    private void logProcessLine(string source, string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        AppLog.Info($"{source}: {line}");

        if (!MemoryReadFailing && IsMemoryReadFailure(line))
        {
            MemoryReadFailing = true;
            AppLog.Info("tosu 读不到 osu! 的内存：信息栏将拿不到游戏内谱面（多半是 lazer 版本比 tosu 新）");
        }
    }

    /// <summary>tosu 读内存失败时的日志特征：<c>… updatePreciseState Couldn't read long at 418</c>。</summary>
    internal static bool IsMemoryReadFailure(string? line) =>
        !string.IsNullOrWhiteSpace(line) && line.Contains("Couldn't read", StringComparison.OrdinalIgnoreCase);
}
