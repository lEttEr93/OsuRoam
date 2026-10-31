using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using OsuRoam.Models;

namespace OsuRoam.Services;

/// <summary>历史推荐记录，保留最近 50 首，供「历史推荐」窗口展示。</summary>
public sealed class ArchiveStore
{
    public const int MaximumEntries = 50;

    private readonly string directory;

    // 面板直接绑定这个集合：RecommendationItem 那边也是 ObservableCollection，
    // 换掉普通 List 之后新记录才能实时出现在已经打开的历史面板里。
    private readonly ObservableCollection<RecommendationArchiveEntry> entries = [];

    public ArchiveStore(string? directory = null) => this.directory = directory ?? StoragePaths.Root;

    private string FilePath => Path.Combine(directory, "history.json");

    public ObservableCollection<RecommendationArchiveEntry> Entries => entries;

    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(FilePath))
                return;
            await using FileStream stream = File.OpenRead(FilePath);
            List<RecommendationArchiveEntry> loaded =
                await JsonSerializer.DeserializeAsync<List<RecommendationArchiveEntry>>(stream) ?? [];
            entries.Clear();
            foreach (RecommendationArchiveEntry entry in loaded.Take(MaximumEntries))
                entries.Add(entry);
        }
        catch (Exception exception)
        {
            AppLog.Error("读取历史推荐失败", exception);
            entries.Clear();
        }
    }

    /// <summary>记一条推荐并返回它，方便调用方补封面和 tag 高亮。</summary>
    public RecommendationArchiveEntry Add(RecommendationItem item)
    {
        // 同一张谱面再次被推荐时，用新记录替换旧记录并移动到最前。
        RecommendationArchiveEntry? existing = entries.FirstOrDefault(entry => entry.BeatmapId == item.BeatmapId);
        if (existing is not null)
            entries.Remove(existing);

        RecommendationArchiveEntry entry = RecommendationArchiveEntry.FromItem(item);
        entries.Insert(0, entry);
        trim();
        return entry;
    }

    public async Task SaveAsync()
    {
        try
        {
            Directory.CreateDirectory(directory);
            await using FileStream stream = File.Create(FilePath);
            await JsonSerializer.SerializeAsync(stream, entries.ToList(), new JsonSerializerOptions { WriteIndented = false });
        }
        catch (Exception exception)
        {
            AppLog.Error("保存历史推荐失败", exception);
        }
    }

    private void trim()
    {
        while (entries.Count > MaximumEntries)
            entries.RemoveAt(entries.Count - 1);
    }
}
