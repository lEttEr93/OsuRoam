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

    public void LoadCover() => Services.CoverLoader.Load(BeatmapsetId, image => Cover = image);

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
    private bool showSkillRadar;

    [JsonIgnore]
    public bool IsDetailVisible => isDetailVisible;

    [JsonIgnore]
    public bool IsSummaryVisible => !isDetailVisible;

    [JsonIgnore]
    public bool HasPattern => Pattern is not null;

    [JsonIgnore]
    public string RadarToggleLabel => showSkillRadar ? "切到键型" : "切到技能";

    [JsonIgnore]
    public IReadOnlyList<string> RadarLabels => showSkillRadar
        ? ManiaPattern.SkillPatternShortLabels
        : ManiaPattern.CorePatternShortLabels;

    [JsonIgnore]
    public IReadOnlyList<double> RadarValues
    {
        get
        {
            if (Pattern is null)
                return [];
            return showSkillRadar && Pattern.SkillAxes.Length == ManiaPattern.SkillPatterns.Length
                ? Pattern.SkillAxes
                : Pattern.ToVector();
        }
    }

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
            lines.Add(showSkillRadar ? $"技能轴 {Pattern.SkillSource}" : $"主导键型 {Pattern.Category}");
            foreach (KeyValuePair<string, double> pair in Pattern.SpecificShares.Take(3))
                lines.Add($"{pair.Key} {pair.Value:P0}");
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

    public void ToggleRadar()
    {
        showSkillRadar = !showSkillRadar;
        notifyDetail();
    }

    private void notifyDetail()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDetailVisible)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSummaryVisible)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPattern)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RadarLabels)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RadarValues)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RadarToggleLabel)));
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
