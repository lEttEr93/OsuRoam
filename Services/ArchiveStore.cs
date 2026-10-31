using System.IO;
using System.Text.Json;
using OsuRoam.Models;

namespace OsuRoam.Services;

/// <summary>历史推荐记录，保留最近 50 首，供「历史推荐」窗口展示。</summary>
public sealed class ArchiveStore
{
    public const int MaximumEntries = 50;

    private readonly string directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsuRoam");

    private List<RecommendationArchiveEntry> entries = [];

    private string FilePath => Path.Combine(directory, "history.json");

    public IReadOnlyList<RecommendationArchiveEntry> Entries => entries;

    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(FilePath))
                return;
            await using FileStream stream = File.OpenRead(FilePath);
            entries = await JsonSerializer.DeserializeAsync<List<RecommendationArchiveEntry>>(stream) ?? [];
            trim();
        }
        catch (Exception exception)
        {
            AppLog.Error("读取历史推荐失败", exception);
            entries = [];
        }
    }

    public void Add(RecommendationItem item)
    {
        // 同一张谱面再次被推荐时，用新记录替换旧记录并移动到最前。
        entries.RemoveAll(entry => entry.BeatmapId == item.BeatmapId);
        entries.Insert(0, RecommendationArchiveEntry.FromItem(item));
        trim();
    }

    public async Task SaveAsync()
    {
        try
        {
            Directory.CreateDirectory(directory);
            await using FileStream stream = File.Create(FilePath);
            await JsonSerializer.SerializeAsync(stream, entries, new JsonSerializerOptions { WriteIndented = false });
        }
        catch (Exception exception)
        {
            AppLog.Error("保存历史推荐失败", exception);
        }
    }

    private void trim()
    {
        if (entries.Count > MaximumEntries)
            entries.RemoveRange(MaximumEntries, entries.Count - MaximumEntries);
    }
}
