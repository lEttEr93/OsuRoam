using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OsuRoam.Models;
using OsuRoam.Services;

namespace OsuRoam;

/// <summary>
/// 历史推荐面板：贴在主窗口右侧的二级界面，跟随主窗口移动/缩放，不单独占任务栏。
/// </summary>
public partial class HistoryWindow : Window
{
    private readonly AppSettings settings;
    private readonly SettingsStore settingsStore;
    private readonly PatternLibrary? patternLibrary;
    private Window? dockOwner;
    private bool hiddenByOwner;

    /// <summary>入场/退场滑动的水平位移量。</summary>
    private const double SlideOffset = 14;

    /// <summary>入场/退场代次：退场完成时若代次变了，说明期间又被打开过，不能隐藏。</summary>
    private int entranceToken;
    private bool exiting;

    public HistoryWindow(
        ObservableCollection<RecommendationArchiveEntry> entries,
        AppSettings settings,
        SettingsStore settingsStore,
        PatternLibrary? patternLibrary = null)
    {
        InitializeComponent();
        this.settings = settings;
        this.settingsStore = settingsStore;
        this.patternLibrary = patternLibrary;

        // 直接绑 store 的可观察集合：新增、替换、超限淘汰都会实时反映到面板上。
        HistoryList.ItemsSource = entries;
        entries.CollectionChanged += (_, _) => updateEmptyHint(entries.Count);
        updateEmptyHint(entries.Count);
        RefreshEntries(entries);
    }

    /// <summary>双击 tag 加入偏好后通知主窗口刷新高亮。</summary>
    public event Action? TagsChanged;

    /// <summary>重新计算 tag 高亮并补齐封面/键型；集合本身实时绑定，不需要重绑 ItemsSource。</summary>
    public void RefreshEntries(IReadOnlyList<RecommendationArchiveEntry> entries)
    {
        foreach (RecommendationArchiveEntry entry in entries)
        {
            entry.RefreshTagInclusion(settings.IncludedTags);
            if (entry.Cover is null)
                entry.LoadCover();
            // 旧记录没有落盘键型，用键型库按 beatmapId 补一份，让详情层对老记录也能用。
            if (entry.Pattern is null && patternLibrary is not null &&
                patternLibrary.TryGet(entry.BeatmapId) is { } pattern)
            {
                entry.ApplyPattern(pattern);
            }
        }
    }

    /// <summary>
    /// 在 Show() 之前调用：把面板内容设成入场起点。
    ///
    /// 注意这里动的是 <c>RootCard.Opacity</c> 而不是 <c>Window.Opacity</c>：
    /// 窗口本身是 AllowsTransparency 的透明窗口，改窗口不透明度会让 WPF 切换
    /// 分层窗口的渲染路径，入场第一帧容易抖一下。
    /// </summary>
    public void PrepareEntrance()
    {
        if (!SystemParameters.ClientAreaAnimation)
            return;

        try
        {
            RootCard.Opacity = 0;
            RootSlide.SetCurrentValue(TranslateTransform.XProperty, SlideOffset);
        }
        catch (Exception exception)
        {
            Services.AppLog.Error("历史面板入场准备失败", exception);
            RootCard.Opacity = 1;
        }
    }

    /// <summary>在 Show() 之后调用：基值已是起点，动画用 HoldEnd 持有终点，完成后回写基值。</summary>
    public void PlayEntrance()
    {
        entranceToken++;
        exiting = false;

        if (!SystemParameters.ClientAreaAnimation)
        {
            RootCard.Opacity = 1;
            RootSlide.SetCurrentValue(TranslateTransform.XProperty, 0.0);
            return;
        }

        try
        {
            DoubleAnimation fade = Controls.Motion.tween(0, 1, 170);
            fade.FillBehavior = FillBehavior.HoldEnd;
            fade.Completed += (_, _) =>
            {
                RootCard.BeginAnimation(UIElement.OpacityProperty, null);
                RootCard.Opacity = 1;
            };
            RootCard.BeginAnimation(UIElement.OpacityProperty, null);
            RootCard.BeginAnimation(UIElement.OpacityProperty, fade);
            Services.RenderDiagnostics.MeasureOnce("history-entrance");

            DoubleAnimation slide = Controls.Motion.tween(SlideOffset, 0, 220);
            slide.FillBehavior = FillBehavior.HoldEnd;
            slide.Completed += (_, _) =>
            {
                RootSlide.BeginAnimation(TranslateTransform.XProperty, null);
                RootSlide.SetCurrentValue(TranslateTransform.XProperty, 0.0);
            };
            RootSlide.BeginAnimation(TranslateTransform.XProperty, null);
            RootSlide.BeginAnimation(TranslateTransform.XProperty, slide);
        }
        catch (Exception exception)
        {
            Services.AppLog.Error("历史面板入场动画失败", exception);
            RootCard.BeginAnimation(UIElement.OpacityProperty, null);
            RootCard.Opacity = 1;
            RootSlide.BeginAnimation(TranslateTransform.XProperty, null);
            RootSlide.SetCurrentValue(TranslateTransform.XProperty, 0.0);
        }
    }

    /// <summary>关闭动画：淡出 + 右移，结束后才真正 Hide。</summary>
    public void PlayExit()
    {
        if (!IsVisible || exiting)
            return;

        if (!SystemParameters.ClientAreaAnimation)
        {
            Hide();
            return;
        }

        exiting = true;
        int token = ++entranceToken;

        try
        {
            DoubleAnimation fade = Controls.Motion.tween(1, 0, 140);
            fade.FillBehavior = FillBehavior.HoldEnd;
            fade.Completed += (_, _) => finishExit(token);
            RootCard.BeginAnimation(UIElement.OpacityProperty, null);
            RootCard.BeginAnimation(UIElement.OpacityProperty, fade);

            DoubleAnimation slide = Controls.Motion.tween(0, SlideOffset, 180);
            slide.FillBehavior = FillBehavior.HoldEnd;
            RootSlide.BeginAnimation(TranslateTransform.XProperty, null);
            RootSlide.BeginAnimation(TranslateTransform.XProperty, slide);
        }
        catch (Exception exception)
        {
            Services.AppLog.Error("历史面板退场动画失败", exception);
            finishExit(token);
        }
    }

    private void finishExit(int token)
    {
        // 退场途中又被打开过就别藏了（token 变了）。
        if (token != entranceToken)
            return;

        exiting = false;
        RootCard.BeginAnimation(UIElement.OpacityProperty, null);
        RootCard.Opacity = 1;
        RootSlide.BeginAnimation(TranslateTransform.XProperty, null);
        RootSlide.SetCurrentValue(TranslateTransform.XProperty, 0.0);
        Hide();
    }

    /// <summary>退场动画是否正在进行。</summary>
    public bool IsExiting => exiting;

    /// <summary>卡片入场；Transform 必须在运行时新建（模板内的 Freezable 会被冻结）。</summary>
    private void HistoryCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement card)
            Controls.Motion.PlayEnter(card);
    }

    private void DetailButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RecommendationArchiveEntry entry })
            entry.ToggleDetail();
    }

    private void RadarToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RecommendationArchiveEntry entry })
            entry.ToggleRadar();
    }

    private void updateEmptyHint(int count) =>
        EmptyHint.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>贴在目标窗口右侧，跟随它移动/缩放，并在它最小化时一起隐藏。</summary>
    public void DockTo(Window target)
    {
        dockOwner = target;
        Owner = target;
        reposition();
        Height = target.ActualHeight;
        target.LocationChanged += (_, _) => reposition();
        target.SizeChanged += (_, _) => reposition();
        target.StateChanged += (_, _) => syncWithOwner();
        target.IsVisibleChanged += (_, _) => syncWithOwner();
        target.Closed += (_, _) => Close();
    }

    private void syncWithOwner()
    {
        if (dockOwner is null)
            return;

        bool ownerUsable = dockOwner.IsVisible && dockOwner.WindowState != WindowState.Minimized;
        if (!ownerUsable)
        {
            if (IsVisible)
            {
                hiddenByOwner = true;
                Hide();
            }
            return;
        }

        if (hiddenByOwner)
        {
            hiddenByOwner = false;
            Show();
        }
        reposition();
    }

    private void reposition()
    {
        if (dockOwner is null)
            return;

        Left = dockOwner.Left + dockOwner.ActualWidth + 6;
        Top = dockOwner.Top;
        Height = dockOwner.ActualHeight;

        // 右侧放不下就挪到左边
        Rect workArea = SystemParameters.WorkArea;
        if (Left + ActualWidth > workArea.Right)
            Left = Math.Max(workArea.Left, dockOwner.Left - ActualWidth - 6);
        if (Top + Height > workArea.Bottom)
            Top = Math.Max(workArea.Top, workArea.Bottom - Height);
    }

    private async void Tag_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !AppSettings.IsMeaningfulTag(tag))
            return;

        // 用 Preview 版事件：Button 会把 MouseDown 标记为 Handled，普通 MouseDoubleClick 不一定触发。
        e.Handled = true;

        if (settings.IncludedTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            return;

        settings.IncludedTags.Add(tag);
        settings.NormalizeTags();
        await settingsStore.SaveAsync(settings);

        // 立刻把本面板里所有同名 tag 的高亮刷掉，否则双击看起来“没反应”。
        if (HistoryList.ItemsSource is IEnumerable<RecommendationArchiveEntry> all)
            RefreshEntries(all.ToList());

        TagsChanged?.Invoke();
    }
}
