namespace OsuRoam.Services;

/// <summary>
/// 把「勾选的键型」翻译成搜索关键字。
///
/// 依据：osu! 的 <c>q=</c> 是全文匹配，官方 wiki 明确写了它会匹配
/// **mapper tags**（<a href="https://osu.ppy.sh/wiki/en/Beatmap_search">Beatmap search</a>）。
/// 实测（用客户端凭据直接打 API）：
/// <c>jack</c> → 50 条里 49 条带 jack 标签、<c>chordjack</c> → 50/50、<c>stream</c> → 48/50、<c>ln</c> → 50/50。
///
/// 所以先带着关键字搜一轮，能立刻拿到「大概率符合」的候选；命中率远高于盲抽。
/// 关键字只是**提示**，最终仍由本地解析出的键型决定排序。
/// </summary>
public static class PatternSearchHints
{
    private static readonly Dictionary<string, string> ByCore = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Stream"] = "stream",
        ["Chordstream"] = "chordstream",
        ["Jacks"] = "jack",
        // 协调 / 密度在 tag 里通常写 ln（锁手、inverse、长条墙都归这类）
        ["Coordination"] = "ln",
        ["Density"] = "ln",
        ["Wildcard"] = "tech"
    };

    /// <summary>本次搜索该带的关键字，去重后最多两个（多个关键字是 AND，越多结果越少）。</summary>
    public static IReadOnlyList<string> For(Models.AppSettings settings)
    {
        if (settings.Mode != "mania")
            return [];

        List<string> hints = [];
        foreach (string core in settings.SelectedPatterns)
        {
            if (ByCore.TryGetValue(core, out string? word) && !hints.Contains(word, StringComparer.Ordinal))
                hints.Add(word);
        }
        if (settings.PreferLn && !hints.Contains("ln", StringComparer.Ordinal))
            hints.Add("ln");

        return hints.Take(2).ToList();
    }
}
