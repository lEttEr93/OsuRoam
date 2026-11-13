using System.Windows;
using System.Windows.Input;
using OsuRoam.Services;

namespace OsuRoam.Controls;

/// <summary>
/// 更新后第一次启动时的小窗：一句话说明这次更新改了什么。
/// 非模态、不抢焦点；点「好」/ 回车 / Esc 关掉。
/// </summary>
public partial class WhatsNewWindow : Window
{
    public WhatsNewWindow()
    {
        InitializeComponent();
        // 主窗每 750ms 会把自己顶到最上层一次（topmost keeper），普通子窗会被压到下面。
        // 这里登记成「有弹窗」：keeper 在弹窗期间不再顶主窗，再加上窗口自己的 Topmost，
        // 就不会被主窗盖住。
        Services.TopmostKeeper.PopupOpened();
        Closed += (_, _) => Services.TopmostKeeper.PopupClosed();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape or Key.Space)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    /// <summary>填内容：标题写版本，正文就是那几行（现在只有一行）。</summary>
    public void ShowEntry(WhatsNew.Entry entry)
    {
        string caption = Loc.IsEnglish ? $"Updated to {entry.Version}" : $"已更新到 {entry.Version}";
        HeadlineText.Text = caption;
        Title = caption;
        BulletList.ItemsSource = WhatsNew.Lines(entry);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => Close();
}
