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
    public string Reason { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public DateTimeOffset RecommendedAtUtc { get; set; }

    [JsonIgnore]
    public string DisplayTitle => $"{Artist} - {Title}";

    [JsonIgnore]
    public string DifficultySummary => (KeyCount > 0 ? $"{KeyCount}K  " : string.Empty) +
                                       $"{Stars:0.00}*  {Bpm:0} BPM  {TimeSpan.FromSeconds(LengthSeconds):m\\:ss}";

    [JsonIgnore]
    public string CoverUrl => $"https://assets.ppy.sh/beatmaps/{BeatmapsetId}/covers/cover.jpg";

    private System.Windows.Media.ImageSource? cover;
    private System.Windows.Media.ImageSource? backdrop;
    private System.Windows.Media.ImageSource? backdropRaw;
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

    /// <summary>和 <see cref="Backdrop"/> 同一张 96px 图，但没有下边缘渐隐。</summary>
    [JsonIgnore]
    public System.Windows.Media.ImageSource? BackdropRaw
    {
        get => backdropRaw;
        private set
        {
            backdropRaw = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BackdropRaw)));
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
        BackdropRaw = art.BackdropRaw;
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
    public IReadOnlyList<string> RadarLabels => ManiaPattern.SkillPatternShortLabels;

    [JsonIgnore]
    public IReadOnlyList<double> RadarValues =>
        Pattern is not null && Pattern.SkillAxes.Length == ManiaPattern.SkillPatterns.Length
            ? Pattern.SkillAxes
            : [];

    [JsonIgnore]
    public IReadOnlyList<double> PatternBars => Pattern?.ToVector() ?? [];

    [JsonIgnore]
    public IReadOnlyList<string> PatternBarLabels => ManiaPattern.CorePatternLabels;

    [JsonIgnore]
    public IReadOnlyList<string> DetailLines
    {
        get
        {
            List<string> lines =
            [
                $"谱师 {Mapper}",
                $"{Stars:0.00}*  {Bpm:0} BPM  {TimeSpan.FromSeconds(LengthSeconds):m\\:ss}  {RankedYear}",
                $"推荐于 {RecommendedLabel}"
            ];
            if (Pattern is null)
            {
                lines.Add("该记录没有键型数据");
                lines.Add("（切到 mania 模式并重新下载谱面后才会记录）");
                return lines;
            }

            lines.Add($"模式 {Pattern.ModeTag}  LN {Pattern.LnRatio:P0}  NPS {Pattern.NotesPerSecond:0.0}");
            lines.Add($"主导 {Pattern.Category} · {Pattern.SkillSource}");
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailLines)));
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
        Reason = item.Reason,
        Tags = [.. item.Tags],
        Pattern = item.Pattern,
        RecommendedAtUtc = DateTimeOffset.UtcNow
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void setField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
