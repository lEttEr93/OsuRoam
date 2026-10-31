using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OsuRoam.Models;

public sealed class RecommendationItem : INotifyPropertyChanged
{
    private DownloadState downloadState;
    private double downloadProgress;

    public required int BeatmapId { get; init; }
    public required int BeatmapsetId { get; init; }
    public required string Artist { get; init; }
    public required string Title { get; init; }
    public required string DifficultyName { get; init; }
    public required string Mapper { get; init; }
    public required double Stars { get; init; }
    public required double Bpm { get; init; }
    public required int LengthSeconds { get; init; }
    public required int RankedYear { get; init; }
    public required double Score { get; init; }
    public required string Reason { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<RecommendationTag> TagItems { get; init; } = [];
    public int KeyCount { get; init; }

    /// <summary>该条目来自推荐历史回收，而不是本次新抓到的候选。</summary>
    public bool IsRecycled { get; init; }
    public string CoverUrl => $"https://assets.ppy.sh/beatmaps/{BeatmapsetId}/covers/cover.jpg";
    public string PreviewUrl => $"https://b.ppy.sh/preview/{BeatmapsetId}.mp3";
    public string DisplayTitle => $"{Artist} - {Title}";
    public string DifficultySummary => (KeyCount > 0 ? $"{KeyCount}K  " : string.Empty) +
                                       $"{Stars:0.00}*  {Bpm:0} BPM  {TimeSpan.FromSeconds(LengthSeconds):m\\:ss}";

    public DownloadState DownloadState
    {
        get => downloadState;
        set => setField(ref downloadState, value);
    }

    public double DownloadProgress
    {
        get => downloadProgress;
        set => setField(ref downloadProgress, value);
    }

    public string DownloadLabel => DownloadState switch
    {
        DownloadState.Queued => "Queued",
        DownloadState.Downloading => $"{DownloadProgress:P0}",
        DownloadState.Downloaded => "Downloaded",
        DownloadState.Importing => "Importing",
        DownloadState.Imported => "Ready",
        DownloadState.Failed => "Failed",
        _ => "Waiting"
    };

    private bool isPreviewPlaying;
    private bool isPreviewLoading;
    private double previewProgress;

    public bool IsPreviewPlaying
    {
        get => isPreviewPlaying;
        set => setField(ref isPreviewPlaying, value);
    }

    public string PreviewButtonContent => IsPreviewPlaying ? "\u23F8" : "\u25B6";

    public bool IsPreviewLoading
    {
        get => isPreviewLoading;
        set => setField(ref isPreviewLoading, value);
    }

    public double PreviewProgress
    {
        get => previewProgress;
        set => setField(ref previewProgress, Math.Clamp(value, 0, 1));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyDownloadLabel() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DownloadLabel)));

    public void RefreshTagInclusion(IReadOnlyCollection<string> includedTags)
    {
        foreach (RecommendationTag tag in TagItems)
            tag.IsIncluded = includedTags.Contains(tag.Name, StringComparer.OrdinalIgnoreCase);
    }

    private void setField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName is nameof(DownloadState) or nameof(DownloadProgress))
            NotifyDownloadLabel();
        if (propertyName == nameof(IsPreviewPlaying))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewButtonContent)));
    }
}

public enum DownloadState
{
    Waiting,
    Queued,
    Downloading,
    Downloaded,
    Importing,
    Imported,
    Failed
}

public sealed class RecommendationTag : INotifyPropertyChanged
{
    private bool isIncluded;

    public RecommendationTag(string name, bool isIncluded)
    {
        Name = name;
        this.isIncluded = isIncluded;
    }

    public string Name { get; }

    public bool IsIncluded
    {
        get => isIncluded;
        set
        {
            if (isIncluded == value)
                return;
            isIncluded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIncluded)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
