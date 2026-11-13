namespace OsuRoam.Services;

/// <summary>
/// 「更新后第一次启动」要展示的更新说明。
///
/// 就地更新成功后，<c>update\last-result.txt</c> 会留下一条 <c>ok</c>，
/// 主窗读到它就把当次版本记下来，启动完成后弹一次这个窗口，然后就不再出现
/// （结果文件在那之前已经被清掉了，所以「只显示一次」是机制保证的，不是靠标记）。
///
/// 发版时在 <see cref="Entries"/> 前面加一条即可，正文一行就够 —— 这扇窗只是告诉用户
/// 「刚才那次更新做了什么」，不承担 changelog 的职责。
/// </summary>
public static class WhatsNew
{
    public sealed record Entry(string Version, IReadOnlyList<string> Chinese, IReadOnlyList<string> English);

    /// <summary>新版本放在最前面。</summary>
    private static readonly Entry[] Entries =
    [
        new Entry("1.5.6.4",
            ["启动更稳、键型页跟模式走、加了「不善长条」"],
            ["Smoother startup, a mode-aware pattern tab, and an LN-heavy filter"]),
        new Entry("1.5.6.3",
            ["优化了推荐算法"],
            ["Improved the recommendation algorithm"])
    ];

    public static Entry? For(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;
        // AppVersion.Display 可能带 beta/fix 后缀，只比数字段。
        string normalized = version.Trim().TrimStart('v', 'V');
        foreach (Entry entry in Entries)
        {
            if (normalized.Equals(entry.Version, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(entry.Version + "-", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(entry.Version + "+", StringComparison.OrdinalIgnoreCase))
                return entry;
        }
        return null;
    }

    public static IReadOnlyList<string> Lines(Entry entry) => Loc.IsEnglish ? entry.English : entry.Chinese;
}
