using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace OsuRoam.Models;

/// <summary>历史推荐里的一条记录。字段与推荐池卡片一一对应，便于复用同一套展示。</summary>
public sealed class RecommendationArchiveEntry : INotifyPropertyChanged
{
    public int BeatmapId { get; set; }
    public int BeatmapsetId { get; set; }
    public string Artist { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string DifficultyName { get; set; } = string.Empty;
    public string Mapper { get; set; } = string.Empty;
    public double Stars { get; set; }
    public double Bpm { get; set; }
    public int LengthSeconds { get; set; }
    public int RankedYear { get; set; }
    public int KeyCount { get; set; }

    /// <summary>
    /// 0.9 之前的记录只存了整串理由。留着它，旧历史才不会丢理由；
    /// 新记录走下面的结构化字段，换语言时会跟着变。
    /// </summary>
    [JsonPropertyName("Reason")]
    public string LegacyReason { get; set; } = string.Empty;

    public ReasonKind ReasonKind { get; set; } = ReasonKind.Explore;
    public double ReasonTagFit { get; set; }
    public double ReasonStarFit { get; set; }
    public string ReasonCategory { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];
    public DateTimeOffset RecommendedAtUtc { get; set; }

    [JsonIgnore]
    public string Reason
    {
        get
        {
            // 结构化字段全空 → 老记录，原样显示当年那句。
            if (ReasonStarFit <= 0 && ReasonCategory.Length == 0)
                return LegacyReason;

            string reason = ReasonKind switch
            {
                ReasonKind.Tags => Services.Loc.F("reason.tags", ReasonTagFit.ToString("P0"), ReasonStarFit.ToString("P0")),
                ReasonKind.Mapper => Services.Loc.F("reason.mapper", ReasonStarFit.ToString("P0")),
                ReasonKind.Rhythm => Services.Loc.F("reason.rhythm", ReasonStarFit.ToString("P0")),
                _ => Services.Loc.F("reason.explore", ReasonStarFit.ToString("P0"))
            };
            return ReasonCategory.Length == 0 ? reason : $"{reason} · {ReasonCategory}";
        }
    }

    [JsonIgnore]
    public string DisplayTitle => $"{Artist} - {Title}";

    [JsonIgnore]
    public string DifficultySummary => (KeyCount > 0 ? $"{KeyCount}K  " : string.Empty) +
                                       $"{Stars:0.00}*  {Bpm:0} BPM  {TimeSpan.FromSeconds(LengthSeconds):m\\:ss}";

    [JsonIgnore]
    public string CoverUrl => $"https://assets.ppy.sh/beatmaps/{BeatmapsetId}/covers/cover.jpg";

    private System.Windows.Media.ImageSource? cover;
    private System.Windows.Media.ImageSource? backdrop;
    private System.Windows.Media.ImageSource? backdropFill;
    private System.Windows.Media.ImageSource? backdropBar;

    /// <summary>由 CoverLoader 用程序自己的 HttpClient 拉下来的封面。</summary>
    [JsonIgnore]
    public System.Windows.Media.ImageSource? Cover
    {
        get => cover;
        private set
        {
            cover = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Cover)));
        }
    }

    /// <summary>低分辨率背影（96px 宽），卡片背景层放大铺开，天然带模糊。</summary>
    [JsonIgnore]
    public System.Windows.Media.ImageSource? Backdrop
    {
        get => backdrop;
        private set
        {
            backdrop = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Backdrop)));
        }
    }

    /// <summary>铺满卡片用的重糊底（96px），只负责给下半部分带颜色。</summary>
    [JsonIgnore]
    public System.Windows.Media.ImageSource? BackdropFill
    {
        get => backdropFill;
        private set
        {
            backdropFill = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BackdropFill)));
        }
    }

    /// <summary>左右羽化过的版本，迷你播放器里居中铺一条用。</summary>
    [JsonIgnore]
    public System.Windows.Media.ImageSource? BackdropBar
    {
        get => backdropBar;
        private set
        {
            backdropBar = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BackdropBar)));
        }
    }

    public void LoadCover() => Services.CoverLoader.Load(BeatmapsetId, art =>
    {
        Cover = art.Cover;
        Backdrop = art.Backdrop;
        BackdropFill = art.Fill;
        BackdropBar = art.Bar;
    });

    [JsonIgnore]
    public string RecommendedLabel => RecommendedAtUtc.ToLocalTime().ToString("MM-dd HH:mm");

    [JsonIgnore]
    public ObservableCollection<RecommendationTag> TagItems { get; } = [];

    public void RefreshTagInclusion(IReadOnlyCollection<string> includedTags)
    {
        if (TagItems.Count != Tags.Count)
        {
            TagItems.Clear();
            foreach (string tag in Tags)
                TagItems.Add(new RecommendationTag(tag, includedTags.Contains(tag, StringComparer.OrdinalIgnoreCase)));
            return;
        }

        foreach (RecommendationTag tag in TagItems)
            tag.IsIncluded = includedTags.Contains(tag.Name, StringComparer.OrdinalIgnoreCase);
    }

    // ================= 详情层（第二层卡片）=================

    /// <summary>键型快照，详情层的雷达图用它。旧记录或非 mania 谱面为 null。</summary>
    public ManiaPattern? Pattern { get; set; }

    private bool isDetailVisible;

    [JsonIgnore]
    public bool IsDetailVisible => isDetailVisible;

    [JsonIgnore]
    public bool IsSummaryVisible => !isDetailVisible;

    [JsonIgnore]
    public bool HasPattern => Pattern is not null;

    /// <summary>只保留 7 轴技能雷达；6 个核心键型占比改用直方图（占比和为 1，雷达图会误导）。</summary>
    [JsonIgnore]
    public IReadOnlyList<string> RadarLabels => Services.Loc.SkillPatternLabels();

    [JsonIgnore]
    public IReadOnlyList<double> RadarValues =>
        Pattern is not null && Pattern.SkillAxes.Length == ManiaPattern.SkillPatterns.Length
            ? Pattern.SkillAxes
            : [];

    [JsonIgnore]
    public IReadOnlyList<double> PatternBars => Pattern?.ToVector() ?? [];

    [JsonIgnore]
    public IReadOnlyList<string> PatternBarLabels => Services.Loc.CorePatternLabels();

    [JsonIgnore]
    public IReadOnlyList<string> DetailLines
    {
        get
        {
            List<string> lines =
            [
                Services.Loc.F("detail.mapper", Mapper),
                $"{Stars:0.00}*  {Bpm:0} BPM  {TimeSpan.FromSeconds(LengthSeconds):m\\:ss}  {RankedYear}",
                Services.Loc.F("detail.recommendedat", RecommendedLabel)
            ];
            if (Pattern is null)
            {
                lines.Add(Services.Loc.T("detail.nopattern"));
                lines.Add(Services.Loc.T("detail.nopattern.hint"));
                return lines;
            }

            lines.Add(Services.Loc.F("detail.pattern", Pattern.ModeTag,
                Pattern.LnRatio.ToString("P0"), Pattern.NotesPerSecond.ToString("0.0")));
            lines.Add(Services.Loc.F("detail.dominant", Pattern.Category,
                Services.Loc.SkillSourceText(Pattern.SkillSource)));
            return lines;
        }
    }

    public void ApplyPattern(ManiaPattern? value)
    {
        Pattern = value;
        notifyDetail();
    }

    public void ToggleDetail()
    {
        isDetailVisible = !isDetailVisible;
        notifyDetail();
    }

    private void notifyDetail()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDetailVisible)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSummaryVisible)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPattern)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RadarLabels)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RadarValues)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PatternBars)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PatternBarLabels)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailLines)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Reason)));
    }

    public static RecommendationArchiveEntry FromItem(RecommendationItem item) => new()
    {
        BeatmapId = item.BeatmapId,
        BeatmapsetId = item.BeatmapsetId,
        Artist = item.Artist,
        Title = item.Title,
        DifficultyName = item.DifficultyName,
        Mapper = item.Mapper,
        Stars = item.Stars,
        Bpm = item.Bpm,
        LengthSeconds = item.LengthSeconds,
        RankedYear = item.RankedYear,
        KeyCount = item.KeyCount,
        ReasonKind = item.ReasonKind,
        ReasonTagFit = item.ReasonTagFit,
        ReasonStarFit = item.ReasonStarFit,
        ReasonCategory = item.ReasonCategory,
        Tags = [.. item.Tags],
        Pattern = item.Pattern,
        RecommendedAtUtc = DateTimeOffset.UtcNow
    };

    /// <summary>界面语言换了：详情行和雷达标签是现拼的。</summary>
    public void NotifyLanguage()
    {
        notifyDetail();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Reason)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void setField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
