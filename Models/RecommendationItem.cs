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

    /// <summary>推荐理由的骨架（哪种匹配），实际文字按当前语言现拼，见 <see cref="Reason"/>。</summary>
    public ReasonKind ReasonKind { get; init; } = ReasonKind.Explore;
    public double ReasonTagFit { get; init; }
    public double ReasonStarFit { get; init; }
    /// <summary>键型匹配时附在理由后面的主导分类，如 "Jumpstream Tech"。</summary>
    public string ReasonCategory { get; init; } = string.Empty;

    public string Reason
    {
        get
        {
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
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<RecommendationTag> TagItems { get; init; } = [];
    public int KeyCount { get; init; }

    /// <summary>该条目来自推荐历史回收，而不是本次新抓到的候选。</summary>
    public bool IsRecycled { get; init; }
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

    private System.Windows.Media.ImageSource? backdrop;

    /// <summary>低分辨率背影（96px 宽），卡片背景层放大铺开，天然带模糊。</summary>
    public System.Windows.Media.ImageSource? Backdrop
    {
        get => backdrop;
        private set
        {
            backdrop = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Backdrop)));
        }
    }

    private System.Windows.Media.ImageSource? backdropFill;
    private System.Windows.Media.ImageSource? backdropBar;

    /// <summary>铺满卡片用的重糊底（96px），只负责给下半部分带颜色。</summary>
    public System.Windows.Media.ImageSource? BackdropFill
    {
        get => backdropFill;
        private set
        {
            backdropFill = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BackdropFill)));
        }
    }

    /// <summary>左右羽化过的版本，迷你播放器里居中铺一条用（边界融进模糊底）。</summary>
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
        DownloadState.Queued => Services.Loc.T("download.queued"),
        DownloadState.Downloading => $"{DownloadProgress:P0}",
        DownloadState.Downloaded => Services.Loc.T("download.downloaded"),
        DownloadState.Importing => Services.Loc.T("download.importing"),
        DownloadState.Imported => Services.Loc.T("download.imported"),
        DownloadState.Failed => Services.Loc.T("download.failed"),
        _ => Services.Loc.T("download.waiting")
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

    // ================= 详情层（第二层卡片）=================

    private ManiaPattern? pattern;
    private bool isDetailVisible;

    public ManiaPattern? Pattern => pattern;

    private BeatmapDetail? detail;

    /// <summary>从 .osz 解析出来的谱面静态详情（CS/OD/物件数/实际 BPM），非 mania 也有。</summary>
    public BeatmapDetail? Detail => detail;

    public bool HasDetail => detail is not null;

    public void ApplyDetail(BeatmapDetail? value)
    {
        detail = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDetail)));
    }

    public bool IsDetailVisible => isDetailVisible;

    public bool IsSummaryVisible => !isDetailVisible;

    public string DetailTitle => DisplayTitle;

    /// <summary>
    /// 只保留 7 轴技能雷达。6 个核心键型的占比加起来是 1（此消彼长），
    /// 用雷达图会让人误读成相互独立，改由 <see cref="PatternBars"/> 的直方图表示。
    /// </summary>
    public IReadOnlyList<string> RadarLabels => Services.Loc.SkillPatternLabels();

    public IReadOnlyList<double> RadarValues =>
        pattern is not null && pattern.SkillAxes.Length == ManiaPattern.SkillPatterns.Length
            ? pattern.SkillAxes
            : [];

    /// <summary>直方图的 6 个核心键型占比（顺序固定，和 <see cref="PatternBarLabels"/> 对齐）。</summary>
    public IReadOnlyList<double> PatternBars => pattern?.ToVector() ?? [];

    public IReadOnlyList<string> PatternBarLabels => Services.Loc.CorePatternLabels();

    public IReadOnlyList<string> DetailLines
    {
        get
        {
            List<string> lines =
            [
                Services.Loc.F("detail.mapper", Mapper),
                $"{Stars:0.00}*  {Bpm:0} BPM  {TimeSpan.FromSeconds(LengthSeconds):m\\:ss}  {RankedYear}"
            ];
            if (pattern is null)
            {
                lines.Add(Services.Loc.T("detail.maniaonly"));
                lines.Add(Services.Loc.T("detail.maniaonly.hint"));
                return lines;
            }

            // 键型占比交给下面的直方图，这里只留比值型描述（雷达图看形状、条看占比）。
            lines.Add(Services.Loc.F("detail.pattern", pattern.ModeTag,
                pattern.LnRatio.ToString("P0"), pattern.NotesPerSecond.ToString("0.0")));
            lines.Add(Services.Loc.F("detail.dominant", pattern.Category,
                Services.Loc.SkillSourceText(pattern.SkillSource)));
            return lines;
        }
    }

    public void ApplyPattern(ManiaPattern? value)
    {
        pattern = value;
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RadarLabels)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RadarValues)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PatternBars)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PatternBarLabels)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailLines)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Reason)));
    }

    /// <summary>界面语言换了：所有现拼的文字重新广播一次。</summary>
    public void NotifyLanguage()
    {
        notifyDetail();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DownloadLabel)));
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

/// <summary>推荐理由的四种骨架，文字在 LocTable 里按语言取。</summary>
public enum ReasonKind
{
    Tags,
    Mapper,
    Rhythm,
    Explore
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
