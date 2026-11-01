using System.Threading;
using System.Windows;
using OsuRoam.Services;

namespace OsuRoam;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "OsuRoam.SingleInstance";

    private Mutex? singleInstance;
    private bool ownsSingleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppLog.Info($"启动 {typeof(App).Assembly.GetName().Version}｜参数：{string.Join(' ', e.Args)}");

        // 配置和缓存是单写入设计（File.Create 独占），两个实例会互相报
        // "Access to the path ... settings.json is denied"，这里直接拦住第二个。
        // 截图 / 诊断这类开发模式用独立的锁名：它们本来就会和用户正在用的实例并存，
        // 不该把「已经有一个在运行了」的弹窗糊到用户脸上。
        bool developmentRun = e.Args.Any(argument =>
            argument.StartsWith("--snapshot=", StringComparison.OrdinalIgnoreCase) ||
            argument is "--diagnose" or "--diagnose-opaque");
        string mutexName = developmentRun
            ? $"{SingleInstanceMutexName}.dev-{Environment.ProcessId}"
            : SingleInstanceMutexName;

        singleInstance = new Mutex(true, mutexName, out bool createdNew);
        ownsSingleInstance = createdNew;
        AppLog.Info($"单实例锁：{(createdNew ? "获得" : "已被占用")}（{mutexName}）");
        if (!createdNew)
        {
            AppLog.Info("另一个实例在运行，本次启动退出");
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

        // 先把内置 Torus 装上；真正的开关在读到配置后再由 MainWindow 应用。
        Typography.Apply(preferOsuFont: true);

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 只有本线程真的持有了锁才能 Release，否则这里会在退出时抛
        // "Object synchronization method was called from an unsynchronized block"。
        if (singleInstance is not null && ownsSingleInstance)
        {
            try { singleInstance.ReleaseMutex(); } catch (Exception exception) { AppLog.Error("释放单实例锁失败", exception); }
            ownsSingleInstance = false;
        }
        singleInstance?.Dispose();
        singleInstance = null;
        base.OnExit(e);
    }
}
