using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace OsuRoam.Services;

public sealed class TopmostKeeper : IDisposable
{
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    private readonly Window window;
    private readonly DispatcherTimer timer;
    private IntPtr handle;
    private bool disposed;
    private bool failureLogged;
    private int suspensionCount;

    public TopmostKeeper(Window window)
    {
        this.window = window;
        timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(750)
        };
        timer.Tick += Timer_Tick;
        window.SourceInitialized += Window_SourceInitialized;
        window.Deactivated += Window_Deactivated;
        window.StateChanged += Window_StateChanged;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        handle = new WindowInteropHelper(window).Handle;
        forceTopmost();
        timer.Start();
        AppLog.Info("已启用全屏置顶保持。");
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        window.Dispatcher.BeginInvoke(DispatcherPriority.Send, forceTopmost);
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (window.WindowState != WindowState.Minimized)
            forceTopmost();
    }

    private void Timer_Tick(object? sender, EventArgs e) => forceTopmost();

    private void forceTopmost()
    {
        if (disposed || suspensionCount > 0 || handle == IntPtr.Zero || !window.IsVisible || window.WindowState == WindowState.Minimized)
            return;

        window.Topmost = true;
        if (!SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder) && !failureLogged)
        {
            failureLogged = true;
            AppLog.Error($"保持浮窗置顶失败，Win32 错误码 {Marshal.GetLastWin32Error()}。");
        }
    }

    public void Suspend()
    {
        if (!disposed)
            suspensionCount++;
    }

    public void Resume()
    {
        if (disposed || suspensionCount == 0)
            return;
        suspensionCount--;
        if (suspensionCount == 0)
            forceTopmost();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        timer.Stop();
        timer.Tick -= Timer_Tick;
        window.SourceInitialized -= Window_SourceInitialized;
        window.Deactivated -= Window_Deactivated;
        window.StateChanged -= Window_StateChanged;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
