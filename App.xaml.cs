using System.Threading;
using System.Windows;
using OsuRoam.Services;

namespace OsuRoam;

public partial class App : Application
{
    private Mutex? singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 配置和缓存是单写入设计（File.Create 独占），两个实例会互相报
        // "Access to the path ... settings.json is denied"，这里直接拦住第二个。
        singleInstance = new Mutex(true, "OsuRoam.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("已经有一个 osu! Roam 在运行了，先关掉它再启动。", "osu! Roam",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 任何漏网的 UI 线程异常至少要先落盘再退出：
        // 1.3.0 的模板动画崩溃当时只留下一条 Event Log，日志里什么都没有。
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error("未处理的界面异常", args.Exception);
        };

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        singleInstance?.ReleaseMutex();
        singleInstance?.Dispose();
        base.OnExit(e);
    }
}
