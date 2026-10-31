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
