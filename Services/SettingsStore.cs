using System.IO;
using System.Text.Json;
using OsuRoam.Models;

namespace OsuRoam.Services;

public sealed class SettingsStore
{
    private readonly string directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsuRoam");

    public string CacheDirectory => Path.Combine(directory, "Beatmaps");
    private string SettingsPath => Path.Combine(directory, "settings.json");

    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            await using FileStream stream = File.OpenRead(SettingsPath);
            AppSettings settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream) ?? new AppSettings();
            settings.DifficultyRanges ??= AppSettings.CreateDefaultDifficultyRanges();
            settings.NormalizeDifficultyRanges();
            settings.NormalizeRecommendationHistory();
            settings.NormalizeTags();
            settings.NormalizeSimpleFilters();
            DifficultyRangeSettings selectedRange = settings.GetDifficultyRange();
            selectedRange.Automatic = settings.AutomaticDifficulty;
            selectedRange.MinimumStars = settings.MinimumStars;
            selectedRange.MaximumStars = settings.MaximumStars;
            AppSettings.NormalizeDifficultyRange(selectedRange);
            if (settings.DownloadMirrorTemplates.Contains("api.nerinyan.moe", StringComparison.OrdinalIgnoreCase) ||
                settings.DownloadMirrorTemplates.Contains("catboy.best", StringComparison.OrdinalIgnoreCase))
            {
                settings.DownloadMirrorTemplates = AppSettings.DefaultDownloadMirrors;
            }
            return settings;
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        settings.NormalizeDifficultyRanges();
        settings.NormalizeSimpleFilters();
        Directory.CreateDirectory(directory);
        await using FileStream stream = File.Create(SettingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions { WriteIndented = true });
    }

    public string ReadSecret(AppSettings settings) => SecretProtector.Unprotect(settings.ProtectedClientSecret);

    public void SetSecret(AppSettings settings, string secret) =>
        settings.ProtectedClientSecret = SecretProtector.Protect(secret);
}
