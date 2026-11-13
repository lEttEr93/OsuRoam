using System.IO;
using System.Text.Json;
using OsuRoam.Models;

namespace OsuRoam.Services;

public sealed class SettingsStore
{
    private readonly string directory = StoragePaths.Root;

    public string CacheDirectory => Path.Combine(directory, "Beatmaps");
    private string SettingsPath => Path.Combine(directory, "settings.json");

    public string SettingsPathForDisplay => SettingsPath;

    /// <summary>读取失败时的原因；界面用它提示，避免静默变成空配置。</summary>
    public string? LoadFailure { get; private set; }

    /// <summary>
    /// 只把上次的窗口位置读出来（不反序列化整个配置）。
    /// 窗口位置必须在 <c>Show()</c> 之前定好，否则会先出现在默认位置再跳一下（维护清单 16）；
    /// 而完整配置要等启动流程里那段异步加载，来不及。
    /// </summary>
    public bool TryReadWindowPlacement(out double left, out double top)
    {
        left = double.NaN;
        top = double.NaN;
        try
        {
            if (!File.Exists(SettingsPath))
                return false;

            using FileStream stream = File.OpenRead(SettingsPath);
            using JsonDocument document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty(nameof(AppSettings.WindowLeft), out JsonElement leftElement) ||
                !document.RootElement.TryGetProperty(nameof(AppSettings.WindowTop), out JsonElement topElement))
                return false;

            left = leftElement.GetDouble();
            top = topElement.GetDouble();
            return !double.IsNaN(left) && !double.IsNaN(top);
        }
        catch (Exception exception)
        {
            AppLog.Error("读取窗口位置失败", exception);
            return false;
        }
    }

    public async Task<AppSettings> LoadAsync()
    {
        LoadFailure = null;
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
        catch (Exception exception)
        {
            // 读取失败不能当"没有配置"处理：那会让界面空白，用户重填后
            // 一保存就把原配置覆盖掉。这里记录原因，并把旧文件备份一份。
            LoadFailure = $"{exception.GetType().Name}: {exception.Message}";
            AppLog.Error($"读取配置失败：{SettingsPath}", exception);
            backupBrokenSettings();
            return new AppSettings();
        }
    }

    private void backupBrokenSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return;
            string backup = $"{SettingsPath}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(SettingsPath, backup, overwrite: true);
            AppLog.Info($"已备份读取失败的配置：{backup}");
        }
        catch (Exception exception)
        {
            AppLog.Error("备份配置失败", exception);
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        settings.NormalizeDifficultyRanges();
        settings.NormalizeSimpleFilters();
        Directory.CreateDirectory(directory);

        // 先写临时文件再原子替换：另一个实例/杀软短暂占用时不会把配置写坏，
        // 独占打开被拒也能靠重试躲过去。
        string tempPath = SettingsPath + ".tmp";
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(settings, new JsonSerializerOptions { WriteIndented = true });

        Exception? last = null;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                await using (FileStream stream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    await stream.WriteAsync(payload);
                File.Move(tempPath, SettingsPath, overwrite: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                last = exception;
                AppLog.Error($"写入配置失败（第 {attempt + 1} 次）：{SettingsPath}", exception);
                await Task.Delay(150 * (attempt + 1));
            }
        }

        throw new IOException($"无法保存配置到 {SettingsPath}：{last?.Message}", last);
    }

    public string ReadSecret(AppSettings settings) => SecretProtector.Unprotect(settings.ProtectedClientSecret);

    public void SetSecret(AppSettings settings, string secret) =>
        settings.ProtectedClientSecret = SecretProtector.Protect(secret);
}
