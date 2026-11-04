namespace OsuRoam.Services;

/// <summary>
/// 把「勾选的键型」翻译成搜索关键字。
///
/// 依据：osu! 的 <c>q=</c> 是全文匹配，官方 wiki 明确写了它会匹配
/// **mapper tags**（<a href="https://osu.ppy.sh/wiki/en/Beatmap_search">Beatmap search</a>）。
/// 实测（用客户端凭据直接打 API）：<c>q=jack</c> 翻 6 页时每页有 17–32 条同时带白名单标签。
///
/// 关键字只是**线索**：它会连带匹配曲名/作者（例如 "Jack" 这种人名），
/// 所以这类候选不靠 tag 判断，而是抓 `.osu` 解析出真实键型后再决定是否入池
/// （见 <see cref="RecommendationEngine"/> 里的 keywordSourced 分支）。
/// </summary>
public static class PatternSearchHints
{
    /// <summary>每个核心键型对应的搜索词，越靠前越准。</summary>
    private static readonly Dictionary<string, string[]> ByCore = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Stream"] = ["stream"],
        ["Chordstream"] = ["chordstream", "jumpstream"],
        ["Jacks"] = ["jack", "chordjack", "minijack"],
        // 协调 / 密度在 tag 里通常写 ln（锁手、inverse、长条墙都归这类）
        ["Coordination"] = ["ln", "handstream"],
        ["Density"] = ["ln", "handstream"],
        ["Wildcard"] = ["tech"]
    };

    /// <summary>一次漫游最多带几个关键字（每个关键字会各翻几页，别把请求数堆太高）。</summary>
    public const int MaximumHints = 3;

    /// <summary>本次搜索该带的关键字，按勾选键型的优先级去重。</summary>
    public static IReadOnlyList<string> For(Models.AppSettings settings)
    {
        if (settings.Mode != "mania")
            return [];

        List<string> hints = [];
        foreach (string core in settings.SelectedPatterns)
        {
            if (!ByCore.TryGetValue(core, out string[]? words))
                continue;
            foreach (string word in words)
            {
                if (!hints.Contains(word, StringComparer.Ordinal))
                    hints.Add(word);
            }
        }
        if (settings.PreferLn && !hints.Contains("ln", StringComparer.Ordinal))
            hints.Add("ln");

        return hints.Take(MaximumHints).ToList();
    }
}
