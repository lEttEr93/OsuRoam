using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OsuRoam.Models;
using OsuRoam.Services;

namespace OsuRoam;

/// <summary>历史推荐窗口：展示最近推荐过的谱面，卡片形态与推荐池一致。</summary>
public partial class HistoryWindow : Window
{
    private readonly AppSettings settings;
    private readonly SettingsStore settingsStore;

    public HistoryWindow(IReadOnlyList<RecommendationArchiveEntry> entries, AppSettings settings, SettingsStore settingsStore)
    {
        InitializeComponent();
        this.settings = settings;
        this.settingsStore = settingsStore;

        foreach (RecommendationArchiveEntry entry in entries)
            entry.RefreshTagInclusion(settings.IncludedTags);
        HistoryList.ItemsSource = entries;
    }

    /// <summary>历史上双击 tag 加入偏好后通知主窗口刷新高亮。</summary>
    public event Action? TagsChanged;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void Tag_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !AppSettings.IsMeaningfulTag(tag))
            return;

        settings.IncludedTags.Add(tag);
        settings.NormalizeTags();
        await settingsStore.SaveAsync(settings);
        e.Handled = true;
        TagsChanged?.Invoke();
    }
}
