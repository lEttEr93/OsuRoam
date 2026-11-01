using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OsuRoam.Controls;

/// <summary>
/// 卡片入场动画。
///
/// 关键坑：`DataTemplate` 里直接声明的 `TranslateTransform` 会在模板密封时被
/// `FrameworkTemplate` 冻结（Freeze），而冻结的 Freezable 不能再挂动画——用
/// `Storyboard.TargetProperty="(UIElement.RenderTransform).(TranslateTransform.Y)"`
/// 会在卡片实例化那一刻抛 XamlParseException（"无法对不可变对象实例进行动画处理"）。
///
/// 所以入场动画必须在运行时给**每个实例**新建一个可变的 Transform 再动它。
/// </summary>
public static class Motion
{
    /// <summary>跟随系统的「减少动画」开关。</summary>
    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    public static void PlayEnter(FrameworkElement element, double fromY = 7, double milliseconds = 300)
    {
        if (!Enabled || element is null)
            return;

        try
        {
            // 元素级动画基值保持 1：动画从 0 走到 1（Stop 后回到基值 1）。
            // 这里不动基值，避免动画被打断时卡片停在透明状态。
            TranslateTransform slide = new();
            element.RenderTransform = slide;
            element.BeginAnimation(UIElement.OpacityProperty, tween(0, 1, milliseconds));
            slide.BeginAnimation(TranslateTransform.YProperty, tween(fromY, 0, milliseconds));

            // 只采第一张卡片的帧间隔：之后的卡片动画不再采样，避免常驻开销。
            Services.RenderDiagnostics.MeasureOnce("card-enter");
        }
        catch (Exception exception)
        {
            // 入场动画是装饰性的，失败绝不能连累界面。
            Services.AppLog.Error("卡片入场动画失败", exception);
        }
    }

    /// <summary>从 from 走到 to，结束后回到属性基值，不把动画值留在属性上。</summary>
    public static DoubleAnimation tween(double from, double to, double milliseconds) =>
        new(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
}
