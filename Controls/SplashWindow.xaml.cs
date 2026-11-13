using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using OsuRoam.Services;

namespace OsuRoam.Controls;

/// <summary>
/// 启动画面（维护清单 19）：图标 + 「osu!Roam」+ 一行小字，淡入 → 停住 → 淡出，合计约 4 秒，
/// 然后主窗才出现。启动期间用户没有可点的地方，也就不会「太早点一下」把主窗卡住。
/// </summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        loadIcon();
        // 动画关掉时不要留一个 0 透明度的窗口在屏幕上。
        if (!Motion.Enabled)
            Opacity = 1;
    }

    /// <summary>
    /// 图标按「png → ico」依次试：这两种在 csproj 里都登记成了 Resource，
    /// 但 pack URI 的写法（带不带 ;component/）对资源解析有影响，试一下最快。
    /// 全都拿不到就只显示文字，绝不让启动画面本身抛异常。
    /// </summary>
    private void loadIcon()
    {
        string[] candidates =
        [
            "pack://application:,,,/OsuRoam;component/Assets/OsuRoam.png",
            "pack://application:,,,/Assets/OsuRoam.png",
            "pack://application:,,,/OsuRoam;component/Assets/OsuRoam.ico",
            "pack://application:,,,/Assets/OsuRoam.ico"
        ];

        foreach (string candidate in candidates)
        {
            try
            {
                IconImage.Source = new BitmapImage(new Uri(candidate));
                AppLog.Info($"启动画面图标：{candidate}");
                return;
            }
            catch (Exception exception)
            {
                AppLog.Info($"启动画面图标不可用：{candidate}（{exception.GetType().Name}）");
            }
        }

        IconImage.Visibility = Visibility.Collapsed;
        AppLog.Info("启动画面：没找到图标资源，只显示文字");
    }

    /// <summary>淡入（约 0.3 秒）。</summary>
    public Task FadeInAsync()
    {
        if (!Motion.Enabled)
            return Task.CompletedTask;

        Opacity = 0;
        DoubleAnimation fade = Motion.tween(0, 1, 320);
        fade.FillBehavior = FillBehavior.HoldEnd;
        BeginAnimation(OpacityProperty, fade);
        return Task.Delay(320);
    }

    /// <summary>淡出（约 0.35 秒），结束后窗口可以关掉。</summary>
    public async Task FadeOutAsync()
    {
        if (!Motion.Enabled)
            return;

        DoubleAnimation fade = Motion.tween(1, 0, 350);
        fade.FillBehavior = FillBehavior.HoldEnd;
        BeginAnimation(OpacityProperty, fade);
        await Task.Delay(350);
    }
}
