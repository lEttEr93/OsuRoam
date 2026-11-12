using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using OsuRoam.Models;

namespace OsuRoam.Controls;

/// <summary>
/// 池子调试窗：把「总池 / 候选池 / 推荐池」三层的当前状态摊开看。
///
/// 它是**只读**的：不改设置、不触发搜索、不下载，只在打开、按 F5、或勾了「自动刷新」时
/// 向主窗要一份快照。<see cref="OsuRoam.MainWindow"/> 那边负责把数据整理成
/// <see cref="PoolsSnapshot"/>，这里只负责显示。
/// </summary>
public partial class PoolsDebugWindow : Window
{
    private readonly DispatcherTimer autoRefresh;
    private Func<PoolsSnapshot>? provider;

    public PoolsDebugWindow()
    {
        InitializeComponent();
        autoRefresh = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        autoRefresh.Tick += (_, _) => Refresh();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F5)
            {
                Refresh();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close();
            }
        };
        Closed += (_, _) => autoRefresh.Stop();
    }

    /// <summary>主窗把「怎么取快照」交进来。</summary>
    public void Attach(Func<PoolsSnapshot> snapshotProvider)
    {
        provider = snapshotProvider;
        Refresh();
    }

    public void Refresh()
    {
        PoolsSnapshot snapshot;
        try
        {
            snapshot = provider?.Invoke() ?? PoolsSnapshot.Empty;
        }
        catch (Exception exception)
        {
            HintText.Text = $"取快照失败：{exception.Message}";
            return;
        }

        HeaderText.Text = snapshot.Header;
        TotalHeader.Text = $"总池（{snapshot.Total.Count}）";
        CandidateHeader.Text = $"候选池（{snapshot.Candidates.Count}，其中推荐池 {snapshot.Recommended.Count}）";
        TotalList.ItemsSource = snapshot.Total;
        // 右栏就是「候选池」：达标的都在这里，已经在推荐池里的会带上引擎算的推荐理由。
        CandidateList.ItemsSource = snapshot.Candidates;
        FooterText.Text = string.Join("　", new[]
        {
            snapshot.TotalNote,
            snapshot.CandidateNote,
            snapshot.RecommendedNote
        }.Where(part => !string.IsNullOrWhiteSpace(part)));
        HintText.Text = $"更新于 {DateTime.Now:HH:mm:ss}";
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => Refresh();

    private void AutoRefreshCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (autoRefresh is null)
            return;
        if (AutoRefreshCheck.IsChecked == true)
            autoRefresh.Start();
        else
            autoRefresh.Stop();
    }
}
