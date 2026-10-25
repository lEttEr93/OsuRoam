using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NAudio.Vorbis;
using NAudio.Wave;
using OsuRoam.Models;
using OsuRoam.Services;

namespace OsuRoam;

public partial class MainWindow : Window
{
    private readonly SettingsStore settingsStore = new();
    private readonly OsuApiClient api = new();
    private readonly RecommendationEngine recommendationEngine = new();
    private readonly TosuClient tosu = new();
    private readonly TosuProcessManager tosuProcess = new();
    private readonly TopmostKeeper topmostKeeper;
    private readonly ObservableCollection<RecommendationItem> recommendations = [];
    private readonly HashSet<int> sessionImportedBeatmapsetIds = [];
    private readonly BeatmapDownloadService downloader;
    private AppSettings settings = new();
    private PlayerProfile? profile;
    private IReadOnlyList<OsuBeatmapset> candidates = [];
    private CancellationTokenSource sessionCancellation = new();
    private bool isRunning;
    private bool isBusy;
    private bool isCollapsed;
    private double expandedHeight = 720;
    private double expandedWidth = 430;
    private readonly StringBuilder logBuffer = new();
    private RecommendationItem? playingItem;
    private RecommendationItem? preparedNextItem;
    private RecommendationItem? currentRoamItem;
    private bool miniNextBusy;
    private bool settingsControlsReady;
    private readonly ObservableCollection<string> visibleTags = [];
    private readonly ObservableCollection<SimpleFilterOption> statusFilterOptions = createStatusFilterOptions();
    private readonly ObservableCollection<SimpleFilterOption> genreFilterOptions = createGenreFilterOptions();
    private readonly ObservableCollection<SimpleFilterOption> languageFilterOptions = createLanguageFilterOptions();
    private bool updatingSimpleFilters;
    private readonly WaveOutEvent previewPlayer = new();
    private readonly HttpClient previewHttp = new() { Timeout = TimeSpan.FromSeconds(20) };
    private RecommendationItem? previewItem;
    private VorbisWaveReader? previewReader;
    private CancellationTokenSource? previewLoadCancellation;
    private string? previewFilePath;
    private bool stoppingPreview;
    private const double starSliderMaximumPosition = 12.0;
    private const double starSliderHandleWidth = 30.0;
    private StarRangeHandle activeStarRangeHandle;

    public MainWindow()
    {
        InitializeComponent();
        topmostKeeper = new TopmostKeeper(this);
        downloader = new BeatmapDownloadService(settingsStore.CacheDirectory);
        RecommendationList.ItemsSource = recommendations;
        TagListBox.ItemsSource = visibleTags;
        StatusFilterItems.ItemsSource = statusFilterOptions;
        GenreFilterItems.ItemsSource = genreFilterOptions;
        LanguageFilterItems.ItemsSource = languageFilterOptions;
        IncludedTagsItems.ItemsSource = settings.IncludedTags;
        ExcludedTagsItems.ItemsSource = settings.ExcludedTags;
        AppLog.EntryAdded += entry => Dispatcher.BeginInvoke(() => appendLog(entry));
        tosu.StatusChanged += status =>
        {
            AppLog.Info($"tosu: {status}");
            Dispatcher.BeginInvoke(() => ConnectionText.Text = status);
        };
        tosuProcess.StatusChanged += status =>
        {
            AppLog.Info($"tosu process: {status}");
            Dispatcher.BeginInvoke(() => ConnectionText.Text = status);
        };
        tosu.SnapshotReceived += snapshot => Dispatcher.BeginInvoke(() => updateGameState(snapshot));
        tosu.PlayStarted += snapshot => Dispatcher.BeginInvoke(async () => await handlePlayStartedAsync(snapshot));
        tosu.PlaySucceeded += snapshot => Dispatcher.BeginInvoke(async () => await handlePlaySucceededAsync(snapshot));
        previewPlayer.PlaybackStopped += (_, _) =>
        {
            if (!stoppingPreview && previewItem is not null)
                Dispatcher.BeginInvoke(stopPreview);
        };
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        AppLog.Info("osu! Roam 启动");
        AppLog.Info($"日志文件：{AppLog.CurrentPath}");
        settings = await settingsStore.LoadAsync();
        refreshTagList();
        applySettingsToControls();
        if (!double.IsNaN(settings.WindowLeft) && !double.IsNaN(settings.WindowTop))
        {
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
        }
        updatePoolVisibility();
        if (settings.AutoStartTosu)
            await ensureTosuReadyAsync(sessionCancellation.Token);
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (isRunning)
        {
            await stopAsync();
            return;
        }
        await rebuildAsync(true);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await rebuildAsync(!isRunning);

    private async Task rebuildAsync(bool startTosu)
    {
        if (isBusy)
            return;
        isBusy = true;
        StartButton.IsEnabled = false;
        try
        {
            sessionCancellation.Cancel();
            sessionCancellation.Dispose();
            sessionCancellation = new CancellationTokenSource();
            settings = readSettingsFromControls();
            settingsStore.SetSecret(settings, ClientSecretBox.Password);
            await settingsStore.SaveAsync(settings);

            StatusText.Text = "正在读取玩家画像...";
            api.Configure(settings.ClientId, ClientSecretBox.Password);
            int? maniaKeys = settings.Mode == "mania" ? settings.ManiaKeys : null;
            profile = await api.BuildProfileAsync(settings.Username, settings.Mode, maniaKeys, sessionCancellation.Token);
            DifficultyRangeSettings difficultyRange = settings.GetDifficultyRange();
settings.AutomaticDifficulty = difficultyRange.Automatic;
            settings.MinimumStars = difficultyRange.MinimumStars;
            settings.MaximumStars = difficultyRange.MaximumStars;
            await settingsStore.SaveAsync(settings);

            string sampleLabel = profile.DifficultySampleCount == 0
                ? " · 无同分类成绩，沿用设置"
                : $" · {profile.DifficultySampleCount} 个成绩样本";
            ProfileText.Text = $"{profile.User.Username} · {difficultyProfileLabel()} · {formatStarRange(settings.MinimumStars, settings.MaximumStars)}{sampleLabel}";
            StatusText.Text = "正在搜索候选谱面...";
            candidates = [];
            recommendations.Clear();
            playingItem = null;
            preparedNextItem = null;
            refreshTagList();

            HashSet<int> candidateIds = [];
            bool roamStarted = false;
            Task? firstPreparation = null;
            await api.SearchCandidatesInBatchesAsync(
                settings,
                settings.MinimumStars,
                settings.MaximumStars,
                async batch =>
                {
                    List<OsuBeatmapset> newSets = batch.Where(set => candidateIds.Add(set.Id)).ToList();
                    if (newSets.Count == 0)
                        return;

                    candidates = candidates.Concat(newSets).ToList();
                    settings.RecordTags(newSets.SelectMany(set => AppSettings.ParseTags(set.Tags)));
                    refreshTagList();
                    await refillPoolAsync();

                    if (!roamStarted && recommendations.Count > 0)
                    {
                        if (startTosu)
                        {
                            await ensureTosuReadyAsync(sessionCancellation.Token);
                            tosu.Start(settings.TosuWebSocketUrl);
                        }

                        isRunning = true;
                        roamStarted = true;
                        StartButton.Content = "停止";
                        if (settings.AutoDownload && recommendations.FirstOrDefault() is { } first)
                            firstPreparation = downloadItemAsync(first, importAfterDownload: settings.AutoImport, "正在准备第一首");
                        else
                            StatusText.Text = $"已载入首批推荐，共 {recommendations.Count} 首，继续补充中";
                    }
                    else if (roamStarted)
                    {
                        StatusText.Text = $"推荐池已更新，共 {recommendations.Count} 首";
                    }
                },
                sessionCancellation.Token);

            await settingsStore.SaveAsync(settings);
            if (firstPreparation is not null)
                await firstPreparation;
            if (!roamStarted)
            {
                StatusText.Text = "当前范围内没有候选谱面";
                if (startTosu)
                    isRunning = false;
            }
            else if (recommendations.Count > 0 && firstPreparation is null)
            {
                StatusText.Text = $"推荐池已生成，共 {recommendations.Count} 首";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "操作已取消";
        }
        catch (Exception exception)
        {
            AppLog.Error("生成推荐池失败", exception);
            StatusText.Text = conciseMessage(exception);
        }
        finally
        {
            isBusy = false;
            StartButton.IsEnabled = true;
        }
    }

    private async Task refillPoolAsync()
    {
        if (profile is null)
            return;

        int missing = Math.Max(0, settings.PoolSize - recommendations.Count);
        bool historyChanged = false;
        if (missing > 0)
        {
            HashSet<int> excluded = [.. recommendations.Select(item => item.BeatmapsetId)];
            List<int> lowerPriority =
            [
                .. settings.RecommendationHistory,
                .. sessionImportedBeatmapsetIds
            ];
            if (settings.ExcludeRecentlyPlayed)
                lowerPriority.AddRange(profile.PlayedBeatmapsetIds());
            IReadOnlyList<RecommendationItem> additions = recommendationEngine.RankAvoidingHistory(
                candidates, profile, settings, settings.MinimumStars, settings.MaximumStars,
                excluded, lowerPriority, missing);
            foreach (RecommendationItem item in additions)
            {
                recommendations.Add(item);
                settings.RecordRecommendation(item.BeatmapsetId);
                historyChanged = true;
            }
        }
        updatePoolVisibility();
        if (historyChanged)
        {
            try
            {
                await settingsStore.SaveAsync(settings);
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to save recommendation history", exception);
            }
        }
    }

    private async Task downloadItemAsync(RecommendationItem item, bool importAfterDownload, string status)
    {
        if (item.DownloadState == DownloadState.Importing)
            return;
        if (item.DownloadState == DownloadState.Imported)
        {
            if (importAfterDownload)
                setCurrentRoamItem(item);
            return;
        }
        try
        {
            StatusText.Text = $"{status}: {item.DisplayTitle}";
            if (item.DownloadState is not DownloadState.Downloading)
            {
                item.DownloadState = DownloadState.Queued;
                await downloader.DownloadAsync(item, settings, sessionCancellation.Token);
            }
            if (importAfterDownload)
            {
                await downloader.ImportAsync(item, settings, sessionCancellation.Token);
                recordSessionImport(item);
                setCurrentRoamItem(item);
                StatusText.Text = $"已交给 {osuClientDisplayName()} 导入";
            }
            else
            {
                StatusText.Text = $"下一首已下载：{item.DisplayTitle}";
            }
        }
        catch (OperationCanceledException)
        {
            item.DownloadState = DownloadState.Waiting;
        }
        catch (Exception exception)
        {
            AppLog.Error($"下载或导入失败：{item.DisplayTitle}", exception);
            StatusText.Text = $"下载失败：{conciseMessage(exception)}";
        }
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RecommendationItem item })
            await downloadItemAsync(item, settings.AutoImport, "正在手动准备");
    }

    private async void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RecommendationItem item })
            return;
        if (ReferenceEquals(preparedNextItem, item))
            preparedNextItem = null;
        recommendations.Remove(item);
        await refillPoolAsync();
        StatusText.Text = "已跳过并补充推荐池";
    }

    private async void MiniNextButton_Click(object sender, RoutedEventArgs e)
    {
        if (miniNextBusy)
            return;

        miniNextBusy = true;
        MiniNextButton.IsEnabled = false;
        MiniNextButton.Content = "...";
        RecommendationItem? previous = currentRoamItem;
        try
        {
            if (!isRunning)
            {
                await rebuildAsync(true);
                if (!ReferenceEquals(previous, currentRoamItem))
                    return;
            }

            RecommendationItem? next = findNextRecommendation();
            if (next is null)
            {
                await refillPoolAsync();
                next = findNextRecommendation();
            }
            if (next is null)
            {
                StatusText.Text = "推荐池暂无下一首";
                return;
            }

            MiniTrackDetailText.Text = "正在下载并导入...";
            await downloadItemAsync(next, importAfterDownload: true, "正在安装下一首");
            if (next.DownloadState != DownloadState.Imported)
                return;

            if (previous is not null && !ReferenceEquals(previous, next))
                recommendations.Remove(previous);
            if (ReferenceEquals(preparedNextItem, next))
                preparedNextItem = null;
            await refillPoolAsync();
            StatusText.Text = $"已导入下一首：{next.DisplayTitle}";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "下一首操作已取消";
        }
        catch (Exception exception)
        {
            AppLog.Error("迷你播放器安装下一首失败", exception);
            StatusText.Text = $"下一首失败：{conciseMessage(exception)}";
        }
        finally
        {
            miniNextBusy = false;
            MiniNextButton.IsEnabled = true;
            MiniNextButton.Content = "下一首";
            refreshMiniPlayer();
        }
    }

    private RecommendationItem? findNextRecommendation()
    {
        if (currentRoamItem is not null)
        {
            int currentIndex = recommendations.IndexOf(currentRoamItem);
            if (currentIndex >= 0 && currentIndex + 1 < recommendations.Count)
                return recommendations[currentIndex + 1];
        }
        return recommendations.FirstOrDefault(item => !ReferenceEquals(item, currentRoamItem));
    }

    private void setCurrentRoamItem(RecommendationItem item)
    {
        currentRoamItem = item;
        MiniPlayerHeader.DataContext = item;
        refreshMiniPlayer();
        AppLog.Info($"Selected {item.DisplayTitle} [{item.DifficultyName}]");
    }

    private async void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RecommendationItem item })
            return;

        if (ReferenceEquals(previewItem, item))
        {
            if (item.IsPreviewPlaying)
            {
                previewPlayer.Pause();
                item.IsPreviewPlaying = false;
            }
            else
            {
                if (previewLoadCancellation is not null)
                    return;

                if (previewReader is null)
                    return;

                previewPlayer.Play();
                item.IsPreviewPlaying = true;
            }
            return;
        }

        stopPreview();
        previewItem = item;
        item.PreviewProgress = 0;
        item.IsPreviewLoading = true;
        CancellationTokenSource loadCancellation = new();
        previewLoadCancellation = loadCancellation;
        try
        {
            string filePath = await downloadPreviewAsync(
                item.PreviewUrl,
                progress => item.PreviewProgress = progress,
                loadCancellation.Token);
            if (loadCancellation.IsCancellationRequested || !ReferenceEquals(previewItem, item))
            {
                deletePreviewFile(filePath);
                return;
            }

            previewFilePath = filePath;
            previewReader = new VorbisWaveReader(filePath);
            previewPlayer.Init(previewReader);
            previewPlayer.Play();
            item.IsPreviewPlaying = true;
        }
        catch (OperationCanceledException) when (loadCancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLog.Error($"Preview failed for {item.DisplayTitle}", exception);
            if (ReferenceEquals(previewItem, item))
                stopPreview();
            else
                loadCancellation.Dispose();
        }
        finally
        {
            if (ReferenceEquals(previewItem, item))
            {
                item.IsPreviewLoading = false;
                item.PreviewProgress = 1;
            }
            if (ReferenceEquals(previewLoadCancellation, loadCancellation))
            {
                previewLoadCancellation = null;
                loadCancellation.Dispose();
            }
        }
    }

    private async Task<string> downloadPreviewAsync(
        string url,
        Action<double> reportProgress,
        CancellationToken cancellationToken)
    {
        string? filePath = null;
        try
        {
            using HttpResponseMessage response = await previewHttp.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 10 * 1024 * 1024)
                throw new InvalidOperationException("No player profile is available.");

            string extension = string.Equals(
                response.Content.Headers.ContentType?.MediaType, "audio/ogg", StringComparison.OrdinalIgnoreCase)
                ? ".ogg"
                : ".mp3";
            filePath = Path.Combine(Path.GetTempPath(), $"osu-roam-preview-{Guid.NewGuid():N}{extension}");
            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream destination = new(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            long? totalLength = response.Content.Headers.ContentLength;
            long downloaded = 0;
            byte[] buffer = new byte[16 * 1024];
            while (true)
            {
                int read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    break;

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloaded += read;
                if (totalLength is > 0)
                    reportProgress(Math.Clamp((double)downloaded / totalLength.Value, 0, 1));
            }
            reportProgress(1);
            return filePath;
        }
        catch
        {
            if (filePath is not null)
                deletePreviewFile(filePath);
            throw;
        }
    }

    private void stopPreview()
    {
        previewLoadCancellation?.Cancel();
        previewLoadCancellation?.Dispose();
        previewLoadCancellation = null;
        stoppingPreview = true;
        previewPlayer.Stop();
        previewReader?.Dispose();
        previewReader = null;
        if (previewItem is not null)
        {
            previewItem.IsPreviewPlaying = false;
            previewItem.IsPreviewLoading = false;
            previewItem.PreviewProgress = 0;
        }
        previewItem = null;
        stoppingPreview = false;
        if (previewFilePath is not null)
        {
            deletePreviewFile(previewFilePath);
            previewFilePath = null;
        }
    }

    private static void deletePreviewFile(string filePath)
    {
        try { File.Delete(filePath); } catch { }
    }

    private async void RecommendationTag_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !AppSettings.IsMeaningfulTag(tag))
            return;

        settings.IncludedTags.Add(tag);
        settings.NormalizeTags();
        await settingsStore.SaveAsync(settings);
        refreshTagList();
        e.Handled = true;
    }

    private void refreshMiniPlayer()
    {
        MiniPlayerHeader.DataContext = currentRoamItem;
        MiniTrackDetailText.Text = currentRoamItem is null
            ? "等待漫游"
            : $"{currentRoamItem.DifficultyName} · {currentRoamItem.DifficultySummary}";
    }

    private async Task handlePlayStartedAsync(TosuSnapshot snapshot)
    {
        if (!isRunning)
            return;
        playingItem = findRecommendation(snapshot);
        if (playingItem is null)
        {
            AppLog.Info($"检测到游玩，但谱面不在推荐池：beatmap={snapshot.BeatmapId}, set={snapshot.BeatmapsetId}");
            return;
        }

        int index = recommendations.IndexOf(playingItem);
        preparedNextItem = index >= 0 && index + 1 < recommendations.Count ? recommendations[index + 1] : null;
        AppLog.Info($"开始游玩：{playingItem.DisplayTitle}；下一首：{preparedNextItem?.DisplayTitle ?? "无"}");
        if (settings.AutoDownload && preparedNextItem is not null)
            await downloadItemAsync(preparedNextItem, importAfterDownload: false, "游玩中预下载下一首");
    }

    private async Task handlePlaySucceededAsync(TosuSnapshot snapshot)
    {
        if (!isRunning)
            return;
        RecommendationItem? played = playingItem ?? findRecommendation(snapshot);
        if (played is null)
            return;

        AppLog.Info($"成功结算：{played.DisplayTitle}，rank={snapshot.ResultRank}, score={snapshot.ResultScore}");
        StatusText.Text = $"成功结算：{played.DisplayTitle}";
        try
        {
            recommendations.Remove(played);
            RecommendationItem? next = preparedNextItem is not null && recommendations.Contains(preparedNextItem)
                ? preparedNextItem
                : recommendations.FirstOrDefault();
            if (next is not null && settings.AutoImport)
            {
                if (settings.AutoDownload && next.DownloadState is not (DownloadState.Downloaded or DownloadState.Imported))
                    await downloader.DownloadAsync(next, settings, sessionCancellation.Token);
                if (next.DownloadState == DownloadState.Downloaded)
                {
                    StatusText.Text = $"成功结算，正在导入：{next.DisplayTitle}";
                    await downloader.ImportAsync(next, settings, sessionCancellation.Token);
                    recordSessionImport(next);
                    setCurrentRoamItem(next);
                }
            }
            await refillPoolAsync();
            StatusText.Text = next is null ? "成功结算，推荐池暂无下一首" : "下一首已导入";
            playingItem = null;
            preparedNextItem = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            AppLog.Error("成功结算后的自动导入失败", exception);
            StatusText.Text = $"自动导入失败：{conciseMessage(exception)}";
        }
    }

    private RecommendationItem? findRecommendation(TosuSnapshot snapshot) => recommendations.FirstOrDefault(item =>
        item.BeatmapId == snapshot.BeatmapId || (snapshot.BeatmapsetId > 0 && item.BeatmapsetId == snapshot.BeatmapsetId));

    private void recordSessionImport(RecommendationItem item)
    {
        if (item.BeatmapsetId > 0)
            sessionImportedBeatmapsetIds.Add(item.BeatmapsetId);
    }

    private void updateGameState(TosuSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.ProfileName) && profile is null)
            ProfileText.Text = snapshot.ProfileName;
        ConnectionText.Text = snapshot.StateName switch
        {
            "play" when snapshot.IsPaused => "tosu · 已暂停",
            "play" when snapshot.Failed => "tosu · 本局失败",
            "play" => "tosu · 游玩中 · 正在预下载",
            "resultScreen" => "tosu · 结算",
            "selectPlay" => "tosu · 选歌",
            _ => "tosu 已连接"
        };
    }

    private async Task stopAsync()
    {
        sessionCancellation.Cancel();
        await tosu.StopAsync();
        isRunning = false;
        StartButton.Content = "开始漫游";
        StatusText.Text = "已停止";
    }

    private async Task<bool> ensureTosuReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            TosuStartResult result = await tosuProcess.EnsureReadyAsync(settings, cancellationToken);
            if (result.ExecutablePath is not null && string.IsNullOrWhiteSpace(settings.TosuExecutablePath))
                TosuPathBox.Text = result.ExecutablePath;
            return result.Ready;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AppLog.Error("自动启动 tosu 失败", exception);
            ConnectionText.Text = "tosu 启动失败";
            StatusText.Text = $"tosu 启动失败：{conciseMessage(exception)}";
            return false;
        }
    }

    private AppSettings readSettingsFromControls()
    {
        AppSettings value = settings;
        value.Username = UsernameBox.Text.Trim();
        value.ClientId = ClientIdBox.Text.Trim();
        value.Mode = (ModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "osu";
        value.ManiaKeys = selectedManiaKeys();
        DifficultyRangeSettings difficultyRange = value.GetDifficultyRange();
        difficultyRange.Automatic = false;
        AppSettings.NormalizeDifficultyRange(difficultyRange);
        value.AutomaticDifficulty = difficultyRange.Automatic;
        value.MinimumStars = difficultyRange.MinimumStars;
        value.MaximumStars = difficultyRange.MaximumStars;
        value.MinimumYear = parseInt(MinimumYearBox, 2007, DateTime.UtcNow.Year);
        value.MaximumYear = parseInt(MaximumYearBox, value.MinimumYear, DateTime.UtcNow.Year + 1);
        value.MinimumLengthSeconds = parseInt(MinimumLengthBox, 0, 3600);
        value.MaximumLengthSeconds = parseInt(MaximumLengthBox, value.MinimumLengthSeconds, 7200);
        value.Exploration = ExplorationSlider.Value;
        value.PoolSize = int.TryParse((PoolSizeBox.SelectedItem as ComboBoxItem)?.Content?.ToString(), out int size) ? size : 5;
        value.ExcludeRecentlyPlayed = ExcludeRecentCheck.IsChecked == true;
        value.AutoDownload = AutoDownloadCheck.IsChecked == true;
        value.AutoImport = AutoImportCheck.IsChecked == true;
        value.OsuClient = (OsuClientBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "lazer";
        value.OsuExecutablePath = OsuPathBox.Text.Trim();
        value.DownloadMirrorTemplates = DownloadTemplateBox.Text.Trim();
        value.DownloadUrlTemplate = value.DownloadMirrorTemplates.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
            ?? value.DownloadUrlTemplate;
        value.TosuWebSocketUrl = TosuUrlBox.Text.Trim();
        value.TosuExecutablePath = TosuPathBox.Text.Trim();
        value.AutoStartTosu = AutoStartTosuCheck.IsChecked == true;
        value.StopManagedTosuOnExit = StopManagedTosuCheck.IsChecked == true;
        if (string.IsNullOrWhiteSpace(value.Username))
            throw new InvalidOperationException("Username and OAuth client ID are required.");
        if (!Uri.TryCreate(value.TosuWebSocketUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("tosu WebSocket URL is required.");
        return value;
    }

    private void applySettingsToControls()
    {
        settingsControlsReady = false;
        UsernameBox.Text = settings.Username;
        ClientIdBox.Text = settings.ClientId;
        ClientSecretBox.Password = settingsStore.ReadSecret(settings);
        MinimumYearBox.Text = settings.MinimumYear.ToString();
        MaximumYearBox.Text = settings.MaximumYear.ToString();
        MinimumLengthBox.Text = settings.MinimumLengthSeconds.ToString();
        MaximumLengthBox.Text = settings.MaximumLengthSeconds.ToString();
        ExplorationSlider.Value = settings.Exploration;
        ExcludeRecentCheck.IsChecked = settings.ExcludeRecentlyPlayed;
        AutoDownloadCheck.IsChecked = settings.AutoDownload;
        AutoImportCheck.IsChecked = settings.AutoImport;
        OsuPathBox.Text = settings.OsuExecutablePath;
        string osuClient = string.Equals(settings.OsuClient, "stable", StringComparison.OrdinalIgnoreCase) ? "stable" : "lazer";
        foreach (ComboBoxItem item in OsuClientBox.Items)
            if (Equals(item.Tag?.ToString(), osuClient)) OsuClientBox.SelectedItem = item;
        DownloadTemplateBox.Text = string.IsNullOrWhiteSpace(settings.DownloadMirrorTemplates)
            ? settings.DownloadUrlTemplate
            : settings.DownloadMirrorTemplates;
        TosuUrlBox.Text = settings.TosuWebSocketUrl;
        TosuPathBox.Text = settings.TosuExecutablePath;
        AutoStartTosuCheck.IsChecked = settings.AutoStartTosu;
        StopManagedTosuCheck.IsChecked = settings.StopManagedTosuOnExit;
        foreach (ComboBoxItem item in ManiaKeysBox.Items)
            if (Equals(item.Tag?.ToString(), settings.ManiaKeys.ToString())) ManiaKeysBox.SelectedItem = item;
        foreach (ComboBoxItem item in ModeBox.Items)
            if (Equals(item.Tag?.ToString(), settings.Mode)) ModeBox.SelectedItem = item;
        foreach (ComboBoxItem item in PoolSizeBox.Items)
            if (Equals(item.Content?.ToString(), settings.PoolSize.ToString())) PoolSizeBox.SelectedItem = item;
        applySimpleFilterControls();
        updateDifficultyControls();
        updateExplorationText();
        settingsControlsReady = true;
    }

    private void ModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!settingsControlsReady)
            return;
        saveCurrentDifficultyRange();
        settings.Mode = (ModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "osu";
        updateDifficultyControls();
    }

    private void ManiaKeysBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!settingsControlsReady)
            return;
        saveCurrentDifficultyRange();
        settings.ManiaKeys = selectedManiaKeys();
        updateDifficultyControls();
    }

    private void saveCurrentDifficultyRange()
    {
        DifficultyRangeSettings range = settings.GetDifficultyRange();
        range.Automatic = false;
        AppSettings.NormalizeDifficultyRange(range);
    }

    private async void SuggestDifficultyButton_Click(object sender, RoutedEventArgs e)
    {
        if (isBusy) return;
        try
        {
            settings = readSettingsFromControls();
            settingsStore.SetSecret(settings, ClientSecretBox.Password);
            SuggestDifficultyButton.IsEnabled = false;
            StatusText.Text = "正在计算推荐星级...";
            api.Configure(settings.ClientId, ClientSecretBox.Password);
            int? maniaKeys = settings.Mode == "mania" ? settings.ManiaKeys : null;
            profile = await api.BuildProfileAsync(settings.Username, settings.Mode, maniaKeys, sessionCancellation.Token);
            DifficultyRangeSettings range = settings.GetDifficultyRange();
            if (profile.DifficultySampleCount > 0)
            {
                range.MinimumStars = Math.Round(profile.SuggestedMinimumStars, 2);
                range.MaximumStars = Math.Round(profile.SuggestedMaximumStars, 2);
                settings.MinimumStars = range.MinimumStars; settings.MaximumStars = range.MaximumStars;
                applyDifficultyRangeToControls(range);
                await settingsStore.SaveAsync(settings);
                StatusText.Text = "已按玩家水平更新星级范围";
            }
            else StatusText.Text = "没有足够成绩样本，保留当前星级范围";
        }
        catch (Exception ex) { StatusText.Text = conciseMessage(ex); }
        finally { SuggestDifficultyButton.IsEnabled = true; }
    }

    private void TagSearchBox_TextChanged(object sender, TextChangedEventArgs e) => refreshTagList();

    private async void StatusFilterCheckBox_Click(object sender, RoutedEventArgs e) =>
        await simpleFilterChangedAsync(statusFilterOptions, (sender as CheckBox)?.DataContext as SimpleFilterOption,
            (values, strict) => { settings.IncludedStatuses = values; settings.StrictStatuses = strict; });

    private async void GenreFilterCheckBox_Click(object sender, RoutedEventArgs e) =>
        await simpleFilterChangedAsync(genreFilterOptions, (sender as CheckBox)?.DataContext as SimpleFilterOption,
            (values, strict) => { settings.IncludedGenres = values; settings.StrictGenres = strict; });

    private async void LanguageFilterCheckBox_Click(object sender, RoutedEventArgs e) =>
        await simpleFilterChangedAsync(languageFilterOptions, (sender as CheckBox)?.DataContext as SimpleFilterOption,
            (values, strict) => { settings.IncludedLanguages = values; settings.StrictLanguages = strict; });

    private async Task simpleFilterChangedAsync(
        ObservableCollection<SimpleFilterOption> options,
        SimpleFilterOption? clicked,
        Action<List<string>, List<string>> applyValues)
    {
        if (!settingsControlsReady || updatingSimpleFilters)
            return;
        updatingSimpleFilters = true;
        try
        {
            SimpleFilterOption all = options[0];
            if (ReferenceEquals(clicked, all))
            {
                all.Selection = SimpleFilterSelection.Included;
                foreach (SimpleFilterOption option in options.Skip(1)) option.Selection = SimpleFilterSelection.None;
            }
            else
            {
                all.Selection = SimpleFilterSelection.None;
                if (!options.Skip(1).Any(option => option.IsSelected)) all.Selection = SimpleFilterSelection.Included;
            }
            applyValues(options.Skip(1).Where(option => option.IsSelected).Select(option => option.Value).ToList(),
                options.Skip(1).Where(option => option.IsStrict).Select(option => option.Value).ToList());
            settings.NormalizeSimpleFilters();
            await settingsStore.SaveAsync(settings);
        }
        finally { updatingSimpleFilters = false; }
    }
    private void applySimpleFilterControls()
    {
        setFilterOptions(statusFilterOptions, settings.IncludedStatuses, settings.StrictStatuses);
        setFilterOptions(genreFilterOptions, settings.IncludedGenres, settings.StrictGenres);
        setFilterOptions(languageFilterOptions, settings.IncludedLanguages, settings.StrictLanguages);
    }

    private static void setFilterOptions(
        ObservableCollection<SimpleFilterOption> options,
        IReadOnlyCollection<string> selectedValues,
        IReadOnlyCollection<string> strictValues)
    {
        bool allSelected = selectedValues.Count == 0;
        options[0].Selection = allSelected ? SimpleFilterSelection.Included : SimpleFilterSelection.None;
        foreach (SimpleFilterOption option in options.Skip(1))
            option.Selection = strictValues.Contains(option.Value, StringComparer.OrdinalIgnoreCase) ? SimpleFilterSelection.Strict :
                selectedValues.Contains(option.Value, StringComparer.OrdinalIgnoreCase) ? SimpleFilterSelection.Included : SimpleFilterSelection.None;
    }
    private static ObservableCollection<SimpleFilterOption> createStatusFilterOptions() =>
    [
        new("all", "All"), new("ranked", "Ranked"), new("approved", "Approved"),
        new("qualified", "Qualified"), new("loved", "Loved"), new("pending", "Pending"),
        new("wip", "WIP"), new("graveyard", "Graveyard")
    ];

    private static ObservableCollection<SimpleFilterOption> createGenreFilterOptions() =>
    [
        new("all", "All"), new("unspecified", "Unspecified"), new("video game", "Video game"),
        new("anime", "Anime"), new("rock", "Rock"), new("pop", "Pop"), new("other", "Other"),
        new("novelty", "Novelty"), new("hip hop", "Hip hop"), new("electronic", "Electronic"),
        new("metal", "Metal"), new("classical", "Classical"), new("folk", "Folk"), new("jazz", "Jazz")
    ];

    private static ObservableCollection<SimpleFilterOption> createLanguageFilterOptions() =>
    [
        new("all", "All"), new("unspecified", "Unspecified"), new("english", "English"),
        new("japanese", "Japanese"), new("chinese", "Chinese"), new("instrumental", "Instrumental"),
        new("korean", "Korean"), new("french", "French"), new("german", "German"),
        new("swedish", "Swedish"), new("spanish", "Spanish"), new("italian", "Italian"),
        new("polish", "Polish"), new("russian", "Russian"), new("other", "Other")
    ];

    private async void IncludeTagsButton_Click(object sender, RoutedEventArgs e) => await updateSelectedTagsAsync(settings.IncludedTags, settings.ExcludedTags);

    private async void ExcludeTagsButton_Click(object sender, RoutedEventArgs e) => await updateSelectedTagsAsync(settings.ExcludedTags, settings.IncludedTags);

    private async void RemoveIncludedTagButton_Click(object sender, RoutedEventArgs e) => await removeTagAsync(settings.IncludedTags, (sender as Button)?.Tag?.ToString());

    private async void RemoveExcludedTagButton_Click(object sender, RoutedEventArgs e) => await removeTagAsync(settings.ExcludedTags, (sender as Button)?.Tag?.ToString());

    private async Task removeTagAsync(List<string> target, string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;
        target.RemoveAll(existing => string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase));
        await settingsStore.SaveAsync(settings);
        refreshTagList();
    }

    private async Task updateSelectedTagsAsync(List<string> target, List<string> opposite)
    {
        foreach (string tag in TagListBox.SelectedItems.Cast<string>())
        {
            if (!target.Contains(tag, StringComparer.OrdinalIgnoreCase)) target.Add(tag);
            opposite.RemoveAll(existing => string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase));
        }
        settings.NormalizeTags();
        await settingsStore.SaveAsync(settings);
        refreshTagList();
    }

    private void refreshTagList()
    {
        if (TagListBox is null || settings is null) return;
        string query = TagSearchBox?.Text?.Trim() ?? string.Empty;
        visibleTags.Clear();
        foreach (string tag in settings.TagPool.Where(tag => query.Length == 0 || tag.Contains(query, StringComparison.OrdinalIgnoreCase)))
            visibleTags.Add(tag);
        IncludedTagsItems.ItemsSource = null;
        IncludedTagsItems.ItemsSource = settings.IncludedTags;
        ExcludedTagsItems.ItemsSource = null;
        ExcludedTagsItems.ItemsSource = settings.ExcludedTags;
        foreach (RecommendationItem item in recommendations)
            item.RefreshTagInclusion(settings.IncludedTags);
    }
    private void updateDifficultyControls()
    {
        ManiaKeysPanel.Visibility = settings.Mode == "mania" ? Visibility.Visible : Visibility.Collapsed;
        applyDifficultyRangeToControls(settings.GetDifficultyRange());
        DifficultyProfileText.Text = difficultyProfileLabel();
    }

    private void applyDifficultyRangeToControls(DifficultyRangeSettings range)
    {
        AppSettings.NormalizeDifficultyRange(range);
        updateStarRangeVisual();
    }

    private void StarRangeControl_SizeChanged(object sender, SizeChangedEventArgs e) => updateStarRangeVisual();

    private void MinimumStarThumb_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => beginStarRangeDrag(StarRangeHandle.Minimum, e);

    private void MaximumStarThumb_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => beginStarRangeDrag(StarRangeHandle.Maximum, e);

    private void StarRangeTrack_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!settingsControlsReady)
            return;

        DifficultyRangeSettings range = settings.GetDifficultyRange();
        double position = starRangePositionAt(e.GetPosition(StarRangeCanvas).X);
        StarRangeHandle handle = Math.Abs(position - range.MinimumStars) <= Math.Abs(position - starRangeMaximumPosition(range.MaximumStars))
            ? StarRangeHandle.Minimum
            : StarRangeHandle.Maximum;
        activeStarRangeHandle = handle;
        setStarRangePosition(handle, position);
        Mouse.Capture(StarRangeCanvas);
        e.Handled = true;
    }

    private void StarRangeCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (activeStarRangeHandle == StarRangeHandle.None || e.LeftButton != MouseButtonState.Pressed)
            return;
        setStarRangePosition(activeStarRangeHandle, starRangePositionAt(e.GetPosition(StarRangeCanvas).X));
    }

    private void StarRangeCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        activeStarRangeHandle = StarRangeHandle.None;
        if (Mouse.Captured == StarRangeCanvas)
            Mouse.Capture(null);
    }

    private void beginStarRangeDrag(StarRangeHandle handle, MouseButtonEventArgs e)
    {
        if (!settingsControlsReady)
            return;
        activeStarRangeHandle = handle;
        Mouse.Capture(StarRangeCanvas);
        e.Handled = true;
    }

    private void setStarRangePosition(StarRangeHandle handle, double position)
    {
        DifficultyRangeSettings range = settings.GetDifficultyRange();
        if (handle == StarRangeHandle.Minimum)
            range.MinimumStars = Math.Min(position, starRangeMaximumPosition(range.MaximumStars));
        else
            range.MaximumStars = position >= starSliderMaximumPosition
                ? AppSettings.UnlimitedMaximumStars
                : Math.Max(range.MinimumStars, position);

        AppSettings.NormalizeDifficultyRange(range);
        settings.AutomaticDifficulty = range.Automatic = false;
        settings.MinimumStars = range.MinimumStars;
        settings.MaximumStars = range.MaximumStars;
        updateStarRangeVisual();
    }

    private void updateStarRangeVisual()
    {
        if (StarRangeCanvas is null || StarRangeTrack is null)
            return;

        DifficultyRangeSettings range = settings.GetDifficultyRange();
        AppSettings.NormalizeDifficultyRange(range);
        double travel = Math.Max(0, StarRangeCanvas.ActualWidth - starSliderHandleWidth);
        double minimumCenter = starSliderHandleWidth / 2 + travel * range.MinimumStars / starSliderMaximumPosition;
        double maximumCenter = starSliderHandleWidth / 2 + travel * starRangeMaximumPosition(range.MaximumStars) / starSliderMaximumPosition;
        Canvas.SetLeft(StarRangeTrack, starSliderHandleWidth / 2);
        StarRangeTrack.Width = travel;
        double trackLeft = starSliderHandleWidth / 2;
        double trackRight = trackLeft + travel;
        Canvas.SetLeft(StarRangeLeftDim, trackLeft);
        StarRangeLeftDim.Width = Math.Max(0, minimumCenter - trackLeft);
        Canvas.SetLeft(StarRangeRightDim, maximumCenter);
        StarRangeRightDim.Width = Math.Max(0, trackRight - maximumCenter);
        Canvas.SetLeft(MinimumStarThumb, minimumCenter - starSliderHandleWidth / 2);
        Canvas.SetLeft(MaximumStarThumb, maximumCenter - starSliderHandleWidth / 2);
        MinimumStarText.Text = range.MinimumStars.ToString("0.0", CultureInfo.InvariantCulture);
            MaximumStarText.Text = AppSettings.IsUnlimitedMaximumStars(range.MaximumStars)
            ? "Any"
            : range.MaximumStars.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private double starRangePositionAt(double x)
    {
        double travel = Math.Max(1, StarRangeCanvas.ActualWidth - starSliderHandleWidth);
        double position = Math.Clamp((x - starSliderHandleWidth / 2) / travel * starSliderMaximumPosition, 0, starSliderMaximumPosition);
        return Math.Round(position * 10, MidpointRounding.AwayFromZero) / 10;
    }

    private static double starRangeMaximumPosition(double maximumStars) => AppSettings.IsUnlimitedMaximumStars(maximumStars)
        ? starSliderMaximumPosition
        : Math.Clamp(maximumStars, AppSettings.MinimumSelectableStars, AppSettings.MaximumFiniteSelectableStars);

    private static string formatStarRange(double minimumStars, double maximumStars) =>
        $"{minimumStars:0.0}-{(AppSettings.IsUnlimitedMaximumStars(maximumStars) ? "any" : maximumStars.ToString("0.0", CultureInfo.InvariantCulture))} stars";

    private enum StarRangeHandle
    {
        None,
        Minimum,
        Maximum
    }

    private int selectedManiaKeys() => int.TryParse((ManiaKeysBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out int keys)
        ? Math.Clamp(keys, 1, 10)
        : 4;

    private string osuClientDisplayName() => string.Equals(settings.OsuClient, "stable", StringComparison.OrdinalIgnoreCase)
        ? "osu!stable"
        : "osu!lazer";

    private string difficultyProfileLabel() => settings.Mode switch
    {
        "taiko" => "taiko",
        "fruits" => "catch",
        "mania" => $"mania {settings.ManiaKeys}K",
        _ => "standard"
    };

    private static bool tryParseDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.TryParse(text, out value);

    private static double parseDouble(TextBox box, double minimum, double maximum)
    {
        if (!tryParseDouble(box.Text, out double value))
            throw new InvalidOperationException($"'{box.Text}' is not a valid number.");
        return Math.Clamp(value, minimum, maximum);
    }

    private static int parseInt(TextBox box, int minimum, int maximum)
    {
        if (!int.TryParse(box.Text, out int value))
            throw new InvalidOperationException($"'{box.Text}' is not a valid integer.");
        return Math.Clamp(value, minimum, maximum);
    }

    private static string conciseMessage(Exception exception)
    {
        Exception current = exception;
        while (current.InnerException is not null)
            current = current.InnerException;
        return current.Message.Length > 150 ? current.Message[..150] : current.Message;
    }

    private void updatePoolVisibility() => EmptyPoolText.Visibility = recommendations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void ExplorationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => updateExplorationText();

    private void ComboBox_DropDownOpened(object sender, EventArgs e) => topmostKeeper.Suspend();

    private void ComboBox_DropDownClosed(object sender, EventArgs e) => topmostKeeper.Resume();

    private void CopyStatusButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(StatusText.Text ?? string.Empty);
            AppLog.Info("已复制状态到剪贴板。");
        }
        catch (Exception exception)
        {
            AppLog.Error("复制状态到剪贴板失败。", exception);
        }
    }

    private void appendLog(string entry)
    {
        logBuffer.AppendLine(entry);
        if (logBuffer.Length > 12000)
            logBuffer.Remove(0, logBuffer.Length - 12000);
    }

    private void updateExplorationText()
    {
        if (ExplorationText is not null)
            ExplorationText.Text = ExplorationSlider.Value.ToString("P0");
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        isCollapsed = !isCollapsed;
        if (isCollapsed)
        {
            expandedHeight = Height;
            expandedWidth = Width;
            MainContent.Visibility = Visibility.Collapsed;
            Footer.Visibility = Visibility.Collapsed;
            NormalHeader.Visibility = Visibility.Collapsed;
            MiniPlayerHeader.Visibility = Visibility.Visible;
            refreshMiniPlayer();
            MinHeight = 64;
            MinWidth = 390;
            Height = 64;
            Width = Math.Max(390, expandedWidth);
            ResizeMode = ResizeMode.NoResize;
            CollapseButton.Content = "+";
            CollapseButton.ToolTip = "展开";
        }
        else
        {
            MainContent.Visibility = Visibility.Visible;
            Footer.Visibility = Visibility.Visible;
            NormalHeader.Visibility = Visibility.Visible;
            MiniPlayerHeader.Visibility = Visibility.Collapsed;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            MinHeight = 300;
            MinWidth = 390;
            Height = Math.Max(300, expandedHeight);
            Width = Math.Max(390, expandedWidth);
            CollapseButton.Content = "−";
            CollapseButton.ToolTip = "迷你播放器";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    protected override async void OnClosed(EventArgs e)
    {
        stopPreview();
        topmostKeeper.Dispose();
        sessionCancellation.Cancel();
        if (settingsControlsReady)
        {
            saveCurrentDifficultyRange();
            DifficultyRangeSettings range = settings.GetDifficultyRange();
            settings.AutomaticDifficulty = range.Automatic;
            settings.MinimumStars = range.MinimumStars;
            settings.MaximumStars = range.MaximumStars;
            settings.TosuExecutablePath = TosuPathBox.Text.Trim();
            settings.AutoStartTosu = AutoStartTosuCheck.IsChecked == true;
            settings.StopManagedTosuOnExit = StopManagedTosuCheck.IsChecked == true;
        }
        settings.WindowLeft = Left;
        settings.WindowTop = Top;
        try { await settingsStore.SaveAsync(settings); } catch { }
        await tosu.DisposeAsync();
        await tosuProcess.ShutdownAsync(settings.StopManagedTosuOnExit);
        downloader.Dispose();
        api.Dispose();
        previewHttp.Dispose();
        previewPlayer.Dispose();
        sessionCancellation.Dispose();
        base.OnClosed(e);
    }
}
