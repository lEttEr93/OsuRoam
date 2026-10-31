using System.Threading;
using System.Windows;

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

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        singleInstance?.ReleaseMutex();
        singleInstance?.Dispose();
        base.OnExit(e);
    }
}
