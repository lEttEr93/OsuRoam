using System.Threading;
using System.Windows;
using OsuRoam.Services;

namespace OsuRoam;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "OsuRoam.SingleInstance";

    /// <summary>「把已经在跑的那个窗口叫到前面」用的命名事件。</summary>
    private const string ActivateEventName = "OsuRoam.Activate";

    private Mutex? singleInstance;
    private bool ownsSingleInstance;
    private EventWaitHandle? activateEvent;

    /// <summary>
    /// 提示气泡出现了 / 收起来了。气泡不是置顶窗口，主窗的周期性置顶会把它盖住，
    /// 所以这两个回调只是去让 TopmostKeeper 那一拍让路。
    /// 处理函数必须在本类里：事件绑定写在 App.xaml 的 Style 里。
    /// </summary>
    private void ToolTip_Opened(object sender, RoutedEventArgs e) => TopmostKeeper.PopupOpened();

    private void ToolTip_Closed(object sender, RoutedEventArgs e) => TopmostKeeper.PopupClosed();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    protected override void OnStartup(StartupEventArgs e)
    {
        // 先把默认语言（中文版跟随系统 / 英文版固定英文）装进 Application.Resources，
        // XAML 里的 {DynamicResource L.xxx} 才有东西可解析；配置里的语言在 MainWindow 读到后再覆盖。
        Loc.Apply(Flavor.IsEnglish ? UiLanguage.English : UiLanguage.System);

        AppLog.Info($"启动 {typeof(App).Assembly.GetName().Version}｜参数：{string.Join(' ', e.Args)}｜语言：{Loc.Code}｜版本：{Flavor.Describe()}");

        // 配置和缓存是单写入设计（File.Create 独占），两个实例会互相报
        // "Access to the path ... settings.json is denied"，这里直接拦住第二个。
        // 截图 / 诊断这类开发模式用独立的锁名：它们本来就会和用户正在用的实例并存，
        // 不该把「已经有一个在运行了」的弹窗糊到用户脸上。
        bool developmentRun = e.Args.Any(argument =>
            argument.StartsWith("--snapshot=", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("--experiment", StringComparison.OrdinalIgnoreCase) ||
            argument is "--diagnose" or "--diagnose-opaque");
        string mutexName = developmentRun
            ? $"{SingleInstanceMutexName}.dev-{Environment.ProcessId}"
            : SingleInstanceMutexName;

        singleInstance = new Mutex(true, mutexName, out bool createdNew);
        ownsSingleInstance = createdNew;
        // 提权重启：旧实例「先拉起新的、再退出」，所以新实例一上来会看到锁还没释放。
        // 这里等它一会儿，否则会把自己当成第二个实例、去叫醒那个马上就要退出的窗口。
        if (!createdNew && e.Args.Any(argument => argument == "--elevated-restart"))
        {
            AppLog.Info("提权重启：等待旧实例释放单实例锁");
            for (int attempt = 0; attempt < 60 && !createdNew; attempt++)
            {
                Thread.Sleep(200);
                singleInstance.Dispose();
                singleInstance = new Mutex(true, mutexName, out createdNew);
            }

            ownsSingleInstance = createdNew;
            AppLog.Info($"提权重启：{(createdNew ? "已接管" : "等不到旧实例，按第二个实例处理")}");
        }

        AppLog.Info($"单实例锁：{(createdNew ? "获得" : "已被占用")}（{mutexName}）");
        if (!createdNew)
        {
            // 带这类参数时用户是「想看点什么」的：不能像双击那样安静退出，否则现场表现是
            // 「我明明加了 --debug-pools，却什么都没多出来」。
            bool wantsOwnWindow = e.Args.Any(argument => argument is
                "--debug-pools" or "--pool-probe" or "--persist-test" or "--update-check");
            if (wantsOwnWindow)
            {
                AppLog.Info("另一个实例在运行；这次带了需要独占的开发参数，提示后退出");
                MessageBox.Show(
                    "已经有一个 osu! Roam 在运行。\n\n"
                    + "池子调试窗要独占单实例锁（它会写配置和推荐历史），请先关掉那个实例，"
                    + "再带 --debug-pools 重新启动。",
                    "osu! Roam",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // 双击第二次不该被弹窗拦住：把已经在跑的那个窗口叫到前面，然后自己安静退出。
            AppLog.Info("另一个实例在运行，唤起它");
            wakeRunningInstance();
            Shutdown();
            return;
        }

        startActivationListener();

        // 任何漏网的 UI 线程异常至少要先落盘再退出：
        // 1.3.0 的模板动画崩溃当时只留下一条 Event Log，日志里什么都没有。
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error("未处理的界面异常", args.Exception);
        };

        // 先把内置字体装上；真正的开关在读到配置后再由 MainWindow 应用。
        Typography.Apply(preferEmbeddedFont: true);

        // 维护清单 19：先放约 4 秒的启动画面，再出主窗。
        // 开发/探针模式不走这条路 —— 它们要的是「几百毫秒内就抓到画面」。
        if (e.Args.Any(argument => argument == "--splash-only"))
        {
            _ = runSplashOnlyAsync();
        }
        else if (IsDevelopmentRun(e.Args))
        {
            ShowMainWindow();
        }
        else
        {
            _ = ShowSplashThenMainAsync();
        }

        base.OnStartup(e);
    }

    /// <summary>需要立刻出主窗的模式：截图、探针、诊断、检查更新……</summary>
    private static bool IsDevelopmentRun(string[] arguments) => arguments.Any(argument =>
        argument.StartsWith("--snapshot", StringComparison.OrdinalIgnoreCase) ||
        argument.StartsWith("--experiment", StringComparison.OrdinalIgnoreCase) ||
        argument.StartsWith("--pool-probe", StringComparison.OrdinalIgnoreCase) ||
        argument.StartsWith("--preview", StringComparison.OrdinalIgnoreCase) ||
        argument is "--diagnose" or "--diagnose-opaque" or "--debug-pools" or "--persist-test"
            or "--update-check" or "--update-auto-test" or "--whats-new" or "--no-splash");

    private void ShowMainWindow()
    {
        if (MainWindow is not null)
        {
            MainWindow.Show();
            return;
        }

        MainWindow window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    /// <summary>启动画面：淡入 → 停约 3 秒 → 淡出，然后主窗出现。</summary>
    private async Task ShowSplashThenMainAsync()
    {
        Controls.SplashWindow? splash = null;
        try
        {
            // 启动画面期间先关掉「最后一个窗口关掉就退出」这条规则：
            // 两个窗口交接的一瞬间很容易被判定成「没窗口了」，整个进程就悄悄退出
            //（现场：只看到启动画面，日志停在「主窗：已显示」，之后什么都没有）。
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            splash = new Controls.SplashWindow();
            splash.Show();
            AppLog.Info("启动画面：已显示，约 4 秒后进主窗");
            await splash.FadeInAsync();
            await Task.Delay(TimeSpan.FromSeconds(3.0));
            await splash.FadeOutAsync();
        }
        catch (Exception exception)
        {
            AppLog.Error("启动画面失败，直接进主窗", exception);
        }

        // 交接：先把启动画面收掉，再创建主窗 —— 中间不留「两个窗口同时在」的那一刻。
        try
        {
            splash?.Close();
            splash = null;
        }
        catch (Exception exception)
        {
            AppLog.Error("关闭启动画面失败", exception);
        }

        try
        {
            ShowMainWindow();
            AppLog.Info($"主窗：已显示（启动画面之后）｜IsVisible={MainWindow?.IsVisible}｜"
                        + $"窗口数={Windows.Count}");
        }
        catch (Exception exception)
        {
            AppLog.Error("主窗显示失败", exception);
            Shutdown(-1);
            return;
        }

        // 交接完成：恢复「关掉主窗就退出」。
        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }

    /// <summary>开发用 --splash-only：只放启动画面、量一下耗时、写日志后退出（不进主窗）。</summary>
    private async Task runSplashOnlyAsync()
    {
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        Controls.SplashWindow splash = new();
        splash.Show();
        await splash.FadeInAsync();
        await Task.Delay(TimeSpan.FromSeconds(3.0));
        await splash.FadeOutAsync();
        splash.Close();
        clock.Stop();
        AppLog.Info($"启动画面自检：从显示到结束 {clock.ElapsedMilliseconds} ms（目标约 4000）");
        Shutdown();
    }

    /// <summary>第二个实例：把前台权限交给已经在跑的那个，然后敲一下事件。</summary>
    private static void wakeRunningInstance()
    {
        try
        {
            // 第二个实例是用户刚点起来的，握着前台权限；转交给第一个，Activate 才会生在顶上
            // 而不是只在任务栏闪一下。
            _ = AllowSetForegroundWindow(-1 /* ASFW_ANY */);
            if (EventWaitHandle.TryOpenExisting(ActivateEventName, out EventWaitHandle? existing))
            {
                using (existing)
                    existing.Set();
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("唤起已有实例失败", exception);
        }
    }

    /// <summary>第一个实例：后台等「唤起」事件，收到就把主窗口叫到前面。</summary>
    private void startActivationListener()
    {
        try
        {
            activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            Thread thread = new(() =>
            {
                while (true)
                {
                    activateEvent.WaitOne();
                    Dispatcher.BeginInvoke(new Action(activateMainWindow));
                }
            })
            {
                IsBackground = true,
                Name = "RoamActivate"
            };
            thread.Start();
        }
        catch (Exception exception)
        {
            AppLog.Error("创建唤起监听失败", exception);
        }
    }

    private void activateMainWindow()
    {
        Window? window = MainWindow;
        if (window is null)
            return;

        try
        {
            if (!window.IsVisible)
                window.Show();
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            window.Activate();
            window.Focus();
            AppLog.Info("已唤起已有实例");
        }
        catch (Exception exception)
        {
            AppLog.Error("唤起窗口失败", exception);
        }
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
        activateEvent?.Dispose();
        activateEvent = null;
        base.OnExit(e);
    }
}
