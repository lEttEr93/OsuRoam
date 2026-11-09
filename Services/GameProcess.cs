using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace OsuRoam.Services;

/// <summary>osu! 进程现在对我们（以及我们拉起的 tosu）的可读性。</summary>
public enum GameProcessAccess
{
    /// <summary>没找到 osu! 进程。</summary>
    NotFound,

    /// <summary>找到了，也能读内存 —— tosu 可以正常工作。</summary>
    Readable,

    /// <summary>找到了，但打不开内存（对方权限更高）。</summary>
    Denied
}

/// <summary>
/// 探测 osu! 进程能不能被读内存。
///
/// 现场：用 UU 加速器启动 osu! 时，游戏是 <c>uu.exe</c> 的子进程、带管理员令牌
/// （探针实测：普通权限下 <c>OpenProcess(PROCESS_VM_READ)</c> 直接返回 ERROR_ACCESS_DENIED）。
/// tosu 靠读内存拿游戏状态，权限比游戏低时只会一直刷
/// <c>failed to query the process, error: -1073741816</c>，界面上就是「认不出游戏」。
/// 这种时候重启 tosu 没用，必须让 Roam 以管理员身份运行（它拉起的 tosu 会跟着提权）。
/// </summary>
public static class GameProcess
{
    private const int ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessVmRead = 0x0010;
    private const int ProcessQueryInformation = 0x0400;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    /// <summary>当前 Roam 是不是以管理员身份运行。</summary>
    public static bool IsElevated { get; } = readElevation();

    private static bool readElevation()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception exception)
        {
            AppLog.Info($"读取提权状态失败：{exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// 找 osu! 进程并判断能不能读内存。同时有多个进程时「能读」优先；
    /// 都读不到就返回第一个读不到的（<paramref name="processId"/> 是它的 PID）。
    /// </summary>
    public static GameProcessAccess Probe(out int processId, out string? executable)
    {
        processId = 0;
        executable = null;

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception exception)
        {
            AppLog.Info($"枚举进程失败，跳过游戏可读性探测：{exception.Message}");
            return GameProcessAccess.NotFound;
        }

        int deniedId = 0;
        string? deniedPath = null;
        try
        {
            foreach (Process process in processes)
            {
                try
                {
                    if (!looksLikeOsu(process.ProcessName))
                        continue;

                    if (canReadMemory(process.Id))
                    {
                        processId = process.Id;
                        executable = tryGetPath(process.Id);
                        return GameProcessAccess.Readable;
                    }

                    if (deniedId == 0)
                    {
                        deniedId = process.Id;
                        deniedPath = tryGetPath(process.Id);
                    }
                }
                catch
                {
                    // 进程刚好退出 / 读不到：跳过。
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        finally
        {
            if (deniedId != 0)
            {
                processId = deniedId;
                executable = deniedPath;
            }
        }

        return deniedId != 0 ? GameProcessAccess.Denied : GameProcessAccess.NotFound;
    }

    /// <summary>lazer 的进程名是 <c>osu!</c>（Velopack 包装后可能叫 <c>osulazer</c>）。</summary>
    private static bool looksLikeOsu(string processName) =>
        processName.Equals("osu!", StringComparison.OrdinalIgnoreCase) ||
        processName.StartsWith("osulazer", StringComparison.OrdinalIgnoreCase);

    /// <summary>正好是 tosu 需要的权限：PROCESS_VM_READ | PROCESS_QUERY_INFORMATION。</summary>
    private static bool canReadMemory(int processId)
    {
        IntPtr handle = OpenProcess(ProcessVmRead | ProcessQueryInformation, false, processId);
        if (handle == IntPtr.Zero)
            return false;

        CloseHandle(handle);
        return true;
    }

    private static string? tryGetPath(int processId)
    {
        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
            return null;

        try
        {
            StringBuilder buffer = new(1024);
            int size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
