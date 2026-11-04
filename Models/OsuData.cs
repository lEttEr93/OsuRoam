using System.Text.Json.Serialization;

namespace OsuRoam.Models;

public sealed class OsuUser
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

}

public sealed class OsuScore
{
    [JsonPropertyName("passed")]
    public bool Passed { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("beatmap")]
    public OsuBeatmap? Beatmap { get; set; }

    [JsonPropertyName("beatmapset")]
    public OsuBeatmapset? Beatmapset { get; set; }
}

public sealed class OsuBeatmapsetSearch
{
    [JsonPropertyName("beatmapsets")]
    public List<OsuBeatmapset> Beatmapsets { get; set; } = [];

    [JsonPropertyName("cursor_string")]
    public string? CursorString { get; set; }

    /// <summary>该查询在服务端命中的总数（osu! 会截到一个上限，仅供参考）。</summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public sealed class OsuBeatmapset
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("creator")]
    public string Creator { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public string Tags { get; set; } = string.Empty;

    [JsonPropertyName("genre")]
    public OsuLookupValue? Genre { get; set; }

    [JsonPropertyName("genre_id")]
    public int? GenreId { get; set; }

    [JsonPropertyName("language")]
    public OsuLookupValue? Language { get; set; }

    [JsonPropertyName("language_id")]
    public int? LanguageId { get; set; }

    [JsonPropertyName("ranked_date")]
    public DateTimeOffset? RankedDate { get; set; }

    [JsonPropertyName("submitted_date")]
    public DateTimeOffset? SubmittedDate { get; set; }

    [JsonPropertyName("beatmaps")]
    public List<OsuBeatmap> Beatmaps { get; set; } = [];
}

public sealed class OsuLookupValue
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class OsuBeatmap
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("beatmapset_id")]
    public int BeatmapsetId { get; set; }

    [JsonPropertyName("difficulty_rating")]
    public double DifficultyRating { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("bpm")]
    public double Bpm { get; set; }

    [JsonPropertyName("total_length")]
    public int TotalLength { get; set; }

    [JsonPropertyName("cs")]
    public double CircleSize { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;
}

public sealed record PlayerProfile(
    OsuUser User,
    IReadOnlyList<OsuScore> RecentScores,
    IReadOnlyList<OsuScore> BestScores,
    double SuggestedMinimumStars,
    double SuggestedMaximumStars,
    /// <summary>已打谱面 BPM 的中位数。**只用于诊断报告**，不参与推荐排序（见 RecommendationEngine）。</summary>
    double PreferredBpm,
    double PreferredLength,
    int DifficultySampleCount,
    IReadOnlyDictionary<string, int> MapperFrequency,
    IReadOnlySet<int> RecentlyPlayedBeatmapsetIds);

public static class PlayerProfileExtensions
{
    public static IReadOnlySet<int> PlayedBeatmapsetIds(this PlayerProfile profile) => profile.RecentScores.Concat(profile.BestScores)
        .Select(score => score.Beatmap?.BeatmapsetId ?? score.Beatmapset?.Id ?? 0).Where(id => id > 0).ToHashSet();
}
