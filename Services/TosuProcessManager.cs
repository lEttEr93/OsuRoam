using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using OsuRoam.Models;

namespace OsuRoam.Services;

public sealed record TosuStartResult(bool Ready, bool StartedByRoam, string? ExecutablePath);

public sealed class TosuProcessManager : IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim startGate = new(1, 1);
    private Process? managedProcess;

    /// <summary>
    /// 把 tosu 放进一个「最后一个句柄关闭就杀掉里面所有进程」的 Job。
    /// 这样 Roam 被强杀（任务管理器 / taskkill / 崩溃）时，tosu 也会跟着消失 ——
    /// 现场就是留了个僵尸 tosu 占着 24050，之后所有 Roam 都连上那个坏实例。
    /// </summary>
    private IntPtr jobHandle;

    public event Action<string>? StatusChanged;

    /// <summary>tosu 是不是我们自己拉起来的（还活着）。</summary>
    public bool IsManagedRunning => managedProcess is { HasExited: false };

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

            addToKillOnCloseJob(process);
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

    // ---- 让 tosu 必定随着 Roam 结束 ----

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    /// <summary>把刚启动的 tosu 加进 Job：Roam 一死（哪怕是强杀），tosu 也会被系统收掉。</summary>
    private void addToKillOnCloseJob(Process process)
    {
        try
        {
            if (jobHandle == IntPtr.Zero)
            {
                jobHandle = CreateJobObject(IntPtr.Zero, null);
                if (jobHandle == IntPtr.Zero)
                    return;

                JobObjectExtendedLimitInformationStructure limits = new()
                {
                    BasicLimitInformation = new JobObjectBasicLimitInformation
                    {
                        LimitFlags = JobObjectLimitKillOnJobClose
                    }
                };
                int size = System.Runtime.InteropServices.Marshal.SizeOf<JobObjectExtendedLimitInformationStructure>();
                if (!SetInformationJobObject(jobHandle, JobObjectExtendedLimitInformation, ref limits, (uint)size))
                    AppLog.Info($"设置 tosu 的 Job 限制失败（错误码 {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}）");
            }

            if (!AssignProcessToJobObject(jobHandle, process.Handle))
                AppLog.Info($"把 tosu 放进 Job 失败（错误码 {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}）：强杀 Roam 时 tosu 可能会残留");
        }
        catch (Exception exception)
        {
            AppLog.Error("把 tosu 放进 Job 时出错", exception);
        }
    }

    public void Dispose()
    {
        // 关掉句柄就会触发 KILL_ON_JOB_CLOSE，把 tosu 一起带走。
        if (jobHandle == IntPtr.Zero)
            return;
        try { CloseHandle(jobHandle); } catch { }
        jobHandle = IntPtr.Zero;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JobObjectExtendedLimitInformationStructure info, uint length);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformationStructure
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
