using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace OsuRoam.Controls;

/// <summary>
/// 滚轮平滑滚动。WPF 的 ScrollViewer 收到滚轮是"跳一格"，观感很硬；
/// 这里把每一格换算成目标偏移，再用 180ms 缓动推过去。
///
/// 实现要点：VerticalOffset 是只读 DP，不能直接做动画目标，所以用一个内嵌的
/// Animator 持有可动画的 Offset，它的变更回调再去驱动 ScrollViewer。
/// </summary>
public static class SmoothScroll
{
    private static readonly ConditionalWeakTable<ScrollViewer, Animator> Animators = [];

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, onIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static void onIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ScrollViewer viewer)
            return;

        viewer.PreviewMouseWheel -= onPreviewMouseWheel;
        if (e.NewValue is true)
            viewer.PreviewMouseWheel += onPreviewMouseWheel;
    }

    private static void onPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer || e.Handled)
            return;

        double scrollable = viewer.ScrollableHeight;
        if (double.IsNaN(scrollable) || scrollable <= 0.5)
            return;

        e.Handled = true;

        // 一次滚轮按视口高度比例走，触控板的小增量也不会窜得太快。
        double step = Math.Clamp(viewer.ViewportHeight * 0.14, 24, 160);
        double current = viewer.VerticalOffset;
        double target = Math.Clamp(current + (e.Delta > 0 ? -step : step), 0, scrollable);

        Animator animator = Animators.GetValue(viewer, v => new Animator(v));
        animator.MoveTo(current, target);
    }

    private sealed class Animator : Animatable
    {
        public static readonly DependencyProperty OffsetProperty = DependencyProperty.Register(
            nameof(Offset), typeof(double), typeof(Animator),
            new PropertyMetadata(0.0, onOffsetChanged));

        private readonly ScrollViewer viewer;
        private bool suppress;

        public Animator(ScrollViewer viewer) => this.viewer = viewer;

        // Freezable 的抽象要求；我们的 Animator 从不被克隆，返回同视图的新实例即可。
        protected override Freezable CreateInstanceCore() => new Animator(viewer);

        public double Offset
        {
            get => (double)GetValue(OffsetProperty);
            set => SetValue(OffsetProperty, value);
        }

        public void MoveTo(double from, double target)
        {
            if (!SystemParameters.ClientAreaAnimation)
            {
                viewer.ScrollToVerticalOffset(target);
                return;
            }

            // 基值先落到终点（动画结束后 Stop 会回到这里），再以当前偏移为起点推过去。
            suppress = true;
            BeginAnimation(OffsetProperty, null);
            Offset = target;
            suppress = false;

            BeginAnimation(OffsetProperty, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
        }

        private static void onOffsetChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            Animator animator = (Animator)target;
            if (animator.suppress || e.NewValue is not double offset || !double.IsFinite(offset))
                return;
            animator.viewer.ScrollToVerticalOffset(offset);
        }
    }
}
