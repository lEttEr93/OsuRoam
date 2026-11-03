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
using System.Windows.Media.Animation;
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
    private readonly TosuFileClient tosuFiles = new();
    private string? ingameInfoKey;
    private RecommendationItem? ingameInfoItem;
    /// <summary>窗口整体的淡入淡出位移，运行时创建（模板里的 Freezable 会被冻结）。</summary>
    private readonly TranslateTransform rootSlide = new();
    private readonly TopmostKeeper topmostKeeper;
    private readonly ObservableCollection<RecommendationItem> recommendations = [];
    private readonly HashSet<int> sessionImportedBeatmapsetIds = [];
    private readonly BeatmapDownloadService downloader;
    private readonly PatternLibrary patternLibrary = new();

    /// <summary>键型分析串行化：它已移到线程池执行，避免多个下载并发写同一份键型库。</summary>
    private readonly SemaphoreSlim patternGate = new(1, 1);
    private readonly ArchiveStore archiveStore = new();
    private MapInfoWindow? mapInfoWindow;
    private HistoryWindow? historyWindow;
    private bool historyWasVisible;
    private bool prefetching;
    private AppSettings settings = new();
    private PlayerProfile? profile;
    private double[]? patternPreference;
    private Func<int, ManiaPattern?>? patternLookup;
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

    // 状态栏文字：存"键 + 参数"而不是成品字符串，换语言时能原地重画（见 setStatus/renderStatus）。
    private string? statusKey;
    private object?[]? statusArgs;
    private string? statusRaw;
    private TosuSnapshot? lastSnapshot;
    private bool hiddenForPlay;
    private bool restoreHistoryAfterPlay;

    public MainWindow()
    {
        InitializeComponent();
        RootCard.RenderTransform = rootSlide;
        MiniTitleText.Text = Loc.T("mini.nobeatmap");
        Loc.Changed += applyLanguage;
        applyDiagnosticWindowMode();
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
        tosu.SnapshotReceived += snapshot => Dispatcher.BeginInvoke(() =>
        {
            updateGameState(snapshot);
            updateInGameInfo(snapshot);
        });
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
        RenderDiagnostics.LogEnvironment();
        if (StoragePaths.Note is not null)
            AppLog.Info($"存储位置：{StoragePaths.Note}");
        settings = await settingsStore.LoadAsync();
        if (settingsStore.LoadFailure is not null)
        {
            string message = Loc.F("dialog.loadfail", settingsStore.LoadFailure, settingsStore.SettingsPathForDisplay);
            AppLog.Error(message);
            setStatus("status.readfail");
            MessageBox.Show(message, "osu! Roam", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        await patternLibrary.LoadAsync();
        await archiveStore.LoadAsync();
        // 扫一遍已安装谱面（lazer 的内容寻址仓库要遍历两万个文件，放后台，别挡启动）
        _ = refreshInstalledLibraryAsync();
        AppLog.Info($"键型库：{patternLibrary.ParsedCount} 条真实解析 / {patternLibrary.Count} 条总计");

        // 开发用：--experiment，跑几个干净场景对比「推荐」和「谱池」的键型分布，写一份报告后退出。
        if (Environment.GetCommandLineArgs().Any(argument =>
                argument.StartsWith("--experiment", StringComparison.OrdinalIgnoreCase)))
        {
            await runExperimentAsync();
            Close();
            return;
        }

        refreshTagList();
        // tag 池：先用内置/缓存的，后台再拉一次公开仓库的（失败不影响任何功能）。
        TagCatalog.Changed += onTagCatalogChanged;
        _ = refreshTagCatalogAsync();
        applySettingsToControls();
        if (!double.IsNaN(settings.WindowLeft) && !double.IsNaN(settings.WindowTop))
        {
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
        }
        updatePoolVisibility();
        if (settings.AutoStartTosu)
            await ensureTosuReadyAsync(sessionCancellation.Token);

        if (Environment.GetCommandLineArgs().Any(arg => arg == "--diagnose"))
            await runDiagnosticsAsync();
        else if (snapshotPath() is { } capturePath)
            await runSnapshotAsync(capturePath);
    }

    /// <summary>--experiment[=bpm]：跑推荐实验并落一份报告，然后退出。</summary>
    private async Task runExperimentAsync()
    {
        api.Configure(settings.ClientId, settingsStore.ReadSecret(settings));
        string mode = "profile";
        foreach (string argument in Environment.GetCommandLineArgs())
        {
            if (argument.StartsWith("--experiment=", StringComparison.OrdinalIgnoreCase))
                mode = argument["--experiment=".Length..].Trim().ToLowerInvariant();
        }

        string path = Path.Combine(StoragePaths.Root, $"experiment-{mode}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        string report;
        try
        {
            if (mode == "ingame")
            {
                report = await runInGameProbeAsync();
            }
            else if (mode == "library")
            {
                System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
                await LocalBeatmapLibrary.RefreshAsync(StoragePaths.Root, CancellationToken.None);
                clock.Stop();
                report = $"已安装谱面扫描（{clock.ElapsedMilliseconds} ms）\n{LocalBeatmapLibrary.Note}\n" +
                         $"BeatmapID 数量：{LocalBeatmapLibrary.InstalledBeatmapIds.Count}\n" +
                         $"谱面集数量：{LocalBeatmapLibrary.InstalledBeatmapsetIds.Count}\n" +
                         $"扫描目录：{string.Join("、", LocalBeatmapLibrary.ResolveRoots())}\n";
            }
            else if (mode == "bpm")
            {
                report = await RecommendationExperiment.RunBpmAsync(api, settings, 4.0, 5.0, 60, sessionCancellation.Token);
            }
            else
            {
                report = await RecommendationExperiment.RunAsync(api, settings, settings.Username, sessionCancellation.Token);
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("推荐实验失败", exception);
            report = $"推荐实验失败：{exception}";
        }

        await File.WriteAllTextAsync(path, report, new UTF8Encoding(false));
        AppLog.Info($"推荐实验报告：{path}");
    }

    /// <summary>开发用：--snapshot=路径，把当前界面渲染成 PNG 后退退出。</summary>
    private static string? snapshotPath()
    {
        foreach (string argument in Environment.GetCommandLineArgs())
        {
            if (argument.StartsWith("--snapshot=", StringComparison.OrdinalIgnoreCase))
                return argument["--snapshot=".Length..].Trim('"');
        }
        return null;
    }

    /// <summary>--scroll=0.9 的解析，没传就是 0（不滚动）。</summary>
    private static double scrollFractionFromArgs()
    {
        foreach (string argument in Environment.GetCommandLineArgs())
        {
            if (argument.StartsWith("--scroll=", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(argument["--scroll=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                return Math.Clamp(value, 0, 1);
            }
        }
        return 0;
    }

    /// <summary>开发用：--snapshot 时套几张假卡片，让截图里有真实内容可看。</summary>
    private async Task runSnapshotAsync(string path)
    {
        if (Environment.GetCommandLineArgs().Any(arg => arg == "--font=system"))
            Typography.Apply(false);

        AppLog.Info($"截图模式：{path}｜{Typography.Describe()}");

        if (Environment.GetCommandLineArgs().Any(arg => arg == "--with-info"))
        {
            await runInfoSnapshotAsync(path);
            return;
        }

        if (Environment.GetCommandLineArgs().Any(arg => arg == "--with-detail"))
        {
            await runDetailSnapshotAsync(path);
            return;
        }

        seedDiagnosticCards();
        await Task.Delay(1800);

        // 开发用：--expand 展开所有设置分组，方便一次截完整个设置区
        if (Environment.GetCommandLineArgs().Any(arg => arg == "--expand"))
        {
            RangeExpander.IsExpanded = true;
            AccountExpander.IsExpanded = true;
            InterfaceExpander.IsExpanded = true;
            AdvancedExpander.IsExpanded = true;
            await Task.Delay(500);
        }

        // 开发用：--scroll-bottom 先把主内容滚到底，方便截设置区；
        // --scroll=0.9 可以指定更精确的位置（0–1）。
        double scrollFraction = Environment.GetCommandLineArgs().Any(arg => arg == "--scroll-bottom")
            ? 0.755
            : scrollFractionFromArgs();
        if (scrollFraction > 0)
        {
            MainContent.ScrollToVerticalOffset(MainContent.ScrollableHeight * scrollFraction);
            await Task.Delay(400);
        }

        RenderDiagnostics.CaptureWindow(this, path);
        Close();
    }

    /// <summary>开发用：折叠成迷你播放器 + 弹出谱面信息窗，各截一张图。</summary>
    private async Task runInfoSnapshotAsync(string path)
    {
        if (!isCollapsed)
            CollapseButton_Click(this, new RoutedEventArgs());

        RecommendationItem item = buildSnapshotItem();
        currentRoamItem = item;
        refreshMiniPlayer();
        // 等曲绘下载完（截图模式是现拉的，本机最慢 3.4s）
        await Task.Delay(4200);

        showMapInfoFor(item);
        await Task.Delay(1200);

        string infoPath = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? path[..^4] + "-info.png"
            : path + "-info.png";
        if (mapInfoWindow is not null)
            RenderDiagnostics.CaptureWindow(mapInfoWindow, infoPath);
        RenderDiagnostics.CaptureWindow(this, path);

        if (Environment.GetCommandLineArgs().Any(arg => arg == "--probe-exit") && mapInfoWindow is not null)
            await mapInfoWindow.ProbeExitAsync();
        else
            mapInfoWindow?.Dismiss();
        Close();
    }

    /// <summary>开发用：把卡片翻到详情层（雷达 + 直方图），需要时再开历史面板截一张。</summary>
    private async Task runDetailSnapshotAsync(string path)
    {
        RecommendationItem item = buildSnapshotItem();
        recommendations.Add(item);
        for (int index = 1; index < 3; index++)
            recommendations.Add(buildSnapshotItem(index));

        // 先拍摘要层（曲绘背景 + 40px 预览按钮），再翻详情层
        // 等久一点：截图模式下三张曲绘是并发现下载的（本机实测最慢 3.3s），太早拍背景还是空的
        await Task.Delay(5200);
        RenderDiagnostics.CaptureWindow(this, path);
        item.ToggleDetail();
        await Task.Delay(500);
        RenderDiagnostics.CaptureWindow(this, path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? path[..^4] + "-detail.png"
            : path + "-detail.png");

        if (!Environment.GetCommandLineArgs().Any(arg => arg == "--with-history"))
        {
            Close();
            return;
        }

        recordArchive(item);
        archiveStore.Entries.FirstOrDefault()?.ToggleDetail();
        HistoryButton_Click(this, new RoutedEventArgs());
        await Task.Delay(900);
        if (historyWindow is not null)
        {
            string historyPath = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? path[..^4] + "-history.png"
                : path + "-history.png";
            RenderDiagnostics.CaptureWindow(historyWindow, historyPath);
        }

        Close();
    }

    /// <summary>截图用的条目：缓存里有真 .osz 就用真数据，没有就造一份接近真实的。</summary>
    private static readonly (int SetId, int MapId, string Artist, string Title, double Stars)[] SnapshotMaps =
    [
        (1411397, 5075177, "Camellia", "Exit This Earth's Atomosphere", 6.42),
        (2242540, 4686608, "TaKo", "Everything is blue", 4.35),
        (591660, 1300612, "Yunomi", "Mentai Cosmic", 4.57)
    ];

    private RecommendationItem buildSnapshotItem(int index = 0)
    {
        (int setId, int mapId, string artist, string title, double stars) = SnapshotMaps[index % SnapshotMaps.Length];
        RecommendationItem item = new()
        {
            BeatmapId = mapId,
            BeatmapsetId = setId,
            Artist = artist,
            Title = title,
            DifficultyName = "[4K] 1.1x",
            Mapper = "Sotarks",
            Stars = stars,
            Bpm = 180,
            LengthSeconds = 252,
            RankedYear = 2019,
            Score = 0.87,
            ReasonStarFit = 0.78,
            ReasonCategory = "Jumpstream Tech",
            KeyCount = 4
        };

        try
        {
            string? osz = Directory.Exists(settingsStore.CacheDirectory)
                ? Directory.EnumerateFiles(settingsStore.CacheDirectory, "*.osz").FirstOrDefault()
                : null;
            if (osz is not null && OszReader.TryReadBeatmapText(osz, 0, out string text, out _))
            {
                item.ApplyDetail(BeatmapDetailReader.Parse(text));
                item.ApplyPattern(ManiaPatternAnalyzer.Analyze(text));
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("截图模式解析缓存谱面失败", exception);
        }

        item.ApplyDetail(item.Detail ?? new BeatmapDetail
        {
            Mode = 3,
            KeyCount = 4,
            CircleSize = 4,
            OverallDifficulty = 8.5,
            ApproachRate = 9.3,
            HPDrainRate = 7,
            Bpm = 180,
            ObjectCount = 1832,
            CircleCount = 1204,
            SliderCount = 0,
            HoldCount = 628,
            Title = item.Title,
            Artist = item.Artist,
            Creator = item.Mapper,
            Version = item.DifficultyName
        });

        item.ApplyPattern(item.Pattern ?? new ManiaPattern
        {
            KeyCount = 4,
            TotalNotes = 1832,
            TotalHolds = 628,
            NotesPerSecond = 6.4,
            LnRatio = 0.12,
            HbRowRatio = 0.04,
            ModeTag = "RC",
            Category = "Jumpstream",
            SkillSource = "MinaCalc",
            CoreShares = new Dictionary<string, double>
            {
                ["Stream"] = 0.46,
                ["Chordstream"] = 0.31,
                ["Jacks"] = 0.13,
                ["Coordination"] = 0.10,
                ["Density"] = 0,
                ["Wildcard"] = 0
            },
            SpecificShares = new Dictionary<string, double>
            {
                ["Jumpstream"] = 0.38,
                ["Stream"] = 0.22,
                ["Minitrills"] = 0.14,
                ["Handstream"] = 0.09
            },
            SkillAxes = [0.82, 0.74, 0.41, 0.55, 0.33, 0.28, 0.61]
        });

        // 截图时把曲绘真的拉下来，否则卡片背景层是空的、看不出效果。
        item.LoadCover();
        return item;
    }

    /// <summary>临时：--diagnose 时造几张假卡片并打开历史面板，用来采帧间隔。</summary>
    private async Task runDiagnosticsAsync()
    {
        AppLog.Info("诊断模式：构造卡片并打开历史面板");
        seedDiagnosticCards();

        await Task.Delay(2600);
        if (historyWindow is null && archiveStore.Entries.Count > 0)
            HistoryButton_Click(this, new RoutedEventArgs());

        // 折叠 / 展开各测一次，用来对比"先隐藏内容"和"先显示内容"的差别。
        await Task.Delay(1900);
        if (!isCollapsed)
        {
            CollapseButton_Click(this, new RoutedEventArgs());
            RenderDiagnostics.MeasureOnce("collapse");
        }

        await Task.Delay(1900);
        if (isCollapsed)
        {
            CollapseButton_Click(this, new RoutedEventArgs());
            RenderDiagnostics.MeasureOnce("expand");
        }
    }

    /// <summary>诊断/截图共用的假卡片。</summary>
    private void seedDiagnosticCards()
    {
        for (int index = 0; index < 6; index++)
        {
            recommendations.Add(new RecommendationItem
            {
                BeatmapId = 900000 + index,
                BeatmapsetId = 900000 + index,
                Artist = $"Diagnostic {index}",
                Title = "Camellia - Exit This Earth's Atomosphere",
                DifficultyName = $"{index + 1}K probe 7.85★",
                Mapper = "Sotarks",
                Stars = 3.5 + index * 0.4,
                Bpm = 180,
                LengthSeconds = 132,
                RankedYear = 2019 + index,
                Score = 0,
                ReasonStarFit = 0.86,
                ReasonCategory = "Jumpstream Tech"
            });
        }
    }

    /// <summary>
    /// 诊断用：把窗口切成不透明，做 AllowsTransparency 的 A/B 对照。
    /// 必须在窗口句柄创建之前设置。
    /// </summary>
    private void applyDiagnosticWindowMode()
    {
        if (!Environment.GetCommandLineArgs().Any(arg => arg == "--diagnose-opaque"))
            return;

        AllowsTransparency = false;
        Background = new SolidColorBrush(Color.FromRgb(0x12, 0x14, 0x16));
        AppLog.Info("诊断模式：窗口改为不透明（对照组）");
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

            setStatus("status.profile");
            api.Configure(settings.ClientId, ClientSecretBox.Password);
            int? maniaKeys = settings.Mode == "mania" ? settings.ManiaKeys : null;
            profile = await api.BuildProfileAsync(settings.Username, settings.Mode, maniaKeys, sessionCancellation.Token);
            patternPreference = settings.Mode == "mania"
                ? PatternPreference.Build(profile, patternLibrary, settings.ManiaKeys)
                : null;
            if (patternPreference is not null)
                AppLog.Info($"键型偏好向量：{string.Join(", ", ManiaPattern.CorePatterns.Select((name, index) => $"{name}={patternPreference[index]:0.00}"))}");
            DifficultyRangeSettings difficultyRange = settings.GetDifficultyRange();
settings.AutomaticDifficulty = difficultyRange.Automatic;
            settings.MinimumStars = difficultyRange.MinimumStars;
            settings.MaximumStars = difficultyRange.MaximumStars;
            await settingsStore.SaveAsync(settings);

            string sampleLabel = profile.DifficultySampleCount == 0
                ? " · " + Loc.T("profile.nosamples")
                : " · " + Loc.F("profile.samples", profile.DifficultySampleCount);
            ProfileText.Text = $"{profile.User.Username} · {difficultyProfileLabel()} · {formatStarRange(settings.MinimumStars, settings.MaximumStars)}{sampleLabel}";
            setStatus("status.search");
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
                        StartButton.Content = Loc.T("btn.stop");
                        if (settings.AutoDownload && recommendations.FirstOrDefault() is { } first)
                            firstPreparation = downloadItemAsync(first, importAfterDownload: settings.AutoImport, "status.preparing");
                        else
                            setStatus("status.loaded", recommendations.Count);
                    }
                    else if (roamStarted)
                    {
                        setStatus("status.updated", recommendations.Count);
                    }
                },
                sessionCancellation.Token);

            // 每次漫游推进排序轮换，让下一次的候选集合与这次不同。
            settings.SearchRotation++;
            await settingsStore.SaveAsync(settings);
            if (firstPreparation is not null)
                await firstPreparation;
            if (!roamStarted)
            {
                setStatus("status.empty");
                if (startTosu)
                    isRunning = false;
            }
            else if (recommendations.Count > 0 && firstPreparation is null)
            {
                setStatus("status.built", recommendations.Count);
            }
        }
        catch (OperationCanceledException)
        {
            setStatus("status.cancelled");
        }
        catch (Exception exception)
        {
            AppLog.Error("生成推荐池失败", exception);
            setStatusText(conciseMessage(exception));
        }
        finally
        {
            isBusy = false;
            StartButton.IsEnabled = true;
        }
    }

    /// <summary>为当前候选谱面准备键型：库里有真实数据就用，没有就用 tag 推断。</summary>
    private void refreshPatternCache()
    {
        if (settings.Mode != "mania")
        {
            patternLookup = null;
            return;
        }

        Dictionary<int, ManiaPattern?> cache = [];
        foreach (OsuBeatmapset set in candidates)
        {
            IReadOnlyList<string> tags = AppSettings.ParseTags(set.Tags);
            foreach (OsuBeatmap beatmap in set.Beatmaps)
            {
                if (!string.Equals(beatmap.Mode, settings.Mode, StringComparison.OrdinalIgnoreCase))
                    continue;
                if ((int)Math.Round(beatmap.CircleSize) != settings.ManiaKeys)
                    continue;
                cache[beatmap.Id] = patternLibrary.Resolve(beatmap.Id, set.Id, tags, settings.ManiaKeys);
            }
        }
        patternLookup = id => cache.TryGetValue(id, out ManiaPattern? pattern) ? pattern : null;
    }

    private async Task refillPoolAsync()
    {
        if (profile is null)
            return;

        refreshPatternCache();

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
                excluded, lowerPriority, missing,
                patternLookup, patternPreference, settings.PatternWeight,
                settings.ExcludeInstalledBeatmaps && LocalBeatmapLibrary.HasData
                    ? LocalBeatmapLibrary.InstalledBeatmapIds
                    : null);
            foreach (RecommendationItem item in additions)
            {
                recommendations.Add(item);
                settings.RecordRecommendation(item.BeatmapsetId);
                item.LoadCover();
                historyChanged = true;
            }
        }
        updatePoolVisibility();
        if (missing > 0 && recommendations.Count > 0 && recommendations.All(item => item.IsRecycled))
            setStatus("status.recycled");
        if (historyChanged)
        {
            try
            {
                await settingsStore.SaveAsync(settings);
                await archiveStore.SaveAsync();
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to save recommendation history", exception);
            }
        }
        _ = prefetchPoolAsync();
    }

    private void DetailButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RecommendationItem item })
            item.ToggleDetail();
    }

    /// <summary>只有「导入」或「开始游玩」才记入历史，单纯进推荐池不算。</summary>
    private void recordArchive(RecommendationItem item)
    {
        RecommendationArchiveEntry entry = archiveStore.Add(item);
        entry.RefreshTagInclusion(settings.IncludedTags);
        entry.LoadCover();
        _ = archiveStore.SaveAsync();
    }

    /// <summary>
    /// 预下载整个推荐池：键型分析需要完整 .osu，只有提前下好，
    /// 详情页的雷达和子型占比才对池内每一首都有数据。
    /// </summary>
    private async Task prefetchPoolAsync()
    {
        if (!settings.AutoDownload || prefetching)
            return;

        RecommendationItem[] pending = recommendations
            .Where(item => item.DownloadState is DownloadState.Waiting or DownloadState.Failed)
            .ToArray();
        if (pending.Length == 0)
            return;

        prefetching = true;
        try
        {
            for (int index = 0; index < pending.Length; index++)
            {
                RecommendationItem item = pending[index];
                if (!recommendations.Contains(item))
                    continue;
                setStatus("status.predownload", index + 1, pending.Length, item.DisplayTitle);
                await downloadItemAsync(item, importAfterDownload: false, "预下载");
            }
            setStatus("status.predownloaded", pending.Length);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            AppLog.Error("预下载推荐池失败", exception);
            setStatus("status.predownloadfail", conciseMessage(exception));
        }
        finally
        {
            prefetching = false;
            trimBeatmapCache();
        }
    }

    /// <summary>超出缓存上限时从旧到新删 .osz，推荐池里的谱面不删。</summary>
    private void trimBeatmapCache()
    {
        try
        {
            long limit = Math.Max(0, settings.CacheLimitMegabytes) * 1024L * 1024L;
            if (limit <= 0)
                return;

            HashSet<int> keep = BeatmapCacheCleaner.KeepSet(recommendations, playingItem ?? currentRoamItem);
            BeatmapCacheCleaner.Result result = BeatmapCacheCleaner.Clean(settingsStore.CacheDirectory, limit, keep);
            if (result.DeletedFiles > 0)
                AppLog.Info($"缓存裁剪：删除 {result.DeletedFiles} 个谱面，释放 {result.FreedBytes / 1024 / 1024} MB，剩余 {result.RemainingBytes / 1024 / 1024} MB");
        }
        catch (Exception exception)
        {
            AppLog.Error("缓存裁剪失败", exception);
        }
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (historyWindow is { IsVisible: true })
        {
            if (historyWindow.IsExiting)
                historyWindow.PlayEntrance();   // 正在退场时再点一次：直接回场
            else
                historyWindow.PlayExit();       // 有淡出动画，结束后才 Hide
            return;
        }

        if (historyWindow is null)
        {
            historyWindow = new HistoryWindow(archiveStore.Entries, settings, settingsStore, patternLibrary);
            historyWindow.TagsChanged += () =>
            {
                refreshTagList();
                foreach (RecommendationItem item in recommendations)
                    item.RefreshTagInclusion(settings.IncludedTags);
                historyWindow?.RefreshEntries(archiveStore.Entries);
            };
            historyWindow.Closed += (_, _) => historyWindow = null;
            historyWindow.DockTo(this);
        }

        // 集合本身实时绑定，这里只重算 tag 高亮/补封面。
        historyWindow.RefreshEntries(archiveStore.Entries);
        historyWindow.PrepareEntrance();
        historyWindow.Show();
        historyWindow.Activate();
        historyWindow.PlayEntrance();
    }

    /// <summary>下载（需要时）并按需导入，返回是否已经真正触发 osu! 导入。</summary>
    private async Task<bool> downloadItemAsync(RecommendationItem item, bool importAfterDownload, string statusKey)
    {
        if (item.DownloadState == DownloadState.Importing)
            return false;
        if (item.DownloadState == DownloadState.Imported)
        {
            if (importAfterDownload)
            {
                setCurrentRoamItem(item);
                showMapInfoFor(item);
            }
            return true;
        }
        try
        {
            setStatus("status.withtitle", Loc.T(statusKey), item.DisplayTitle);
            string oszPath = Path.Combine(settingsStore.CacheDirectory, $"{item.BeatmapsetId}.osz");
            bool wantPattern = settings.Mode == "mania";

            // 已经下好的不再走下载（否则会排在预下载后面白等），只有真正没下过才下。
            if (item.DownloadState is not (DownloadState.Downloaded or DownloadState.Downloading))
            {
                item.DownloadState = DownloadState.Queued;
                await downloader.DownloadAsync(item, settings, sessionCancellation.Token);
            }

            bool fileReady = File.Exists(oszPath) && new FileInfo(oszPath).Length >= 1024;
            if (fileReady && (item.Detail is null || (wantPattern && item.Pattern is null)))
            {
                // 谱面详情（CS/OD/AR/物件数）和键型都来自同一个 .osu：解压 + 解析要几十到几百毫秒，
                // 放 UI 线程会吃掉动画帧预算，所以丢到线程池；键型那块还要串行化，避免并发写同一份键型库。
                await patternGate.WaitAsync();
                try
                {
                    System.Diagnostics.Stopwatch parseClock = System.Diagnostics.Stopwatch.StartNew();
                    (BeatmapDetail? detail, string? osuText) parsed = await Task.Run(() =>
                    {
                        OszReader.TryReadBeatmapText(oszPath, item.BeatmapId, out string text, out _);
                        return (BeatmapDetailReader.Parse(text), wantPattern ? text : null);
                    });
                    long parseMilliseconds = parseClock.ElapsedMilliseconds;
                    if (parseMilliseconds > 120)
                        AppLog.Info($"谱面解析较慢：{item.DisplayTitle}，{parseMilliseconds}ms（已在后台线程）");

                    item.ApplyDetail(parsed.detail);
                    if (parsed.osuText is not null &&
                        patternLibrary.Ingest(parsed.osuText, item.BeatmapId, item.BeatmapsetId))
                    {
                        refreshPatternCache();
                        item.ApplyPattern(patternLibrary.TryGet(item.BeatmapId));
                        await patternLibrary.SaveAsync();
                    }
                }
                finally
                {
                    patternGate.Release();
                }
            }
            if (importAfterDownload)
            {
                await downloader.ImportAsync(item, settings, sessionCancellation.Token);
                recordSessionImport(item);
                setCurrentRoamItem(item);
                recordArchive(item);
                setStatus("status.handedover", osuClientDisplayName());
                showMapInfoFor(item);
            }
            else
            {
                setStatus("status.nextdownloaded", item.DisplayTitle);
            }

            return item.DownloadState == DownloadState.Imported;
        }
        catch (OperationCanceledException)
        {
            item.DownloadState = DownloadState.Waiting;
            return false;
        }
        catch (Exception exception)
        {
            AppLog.Error($"下载或导入失败：{item.DisplayTitle}", exception);
            setStatus("status.downloadfail", conciseMessage(exception));
            return false;
        }
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RecommendationItem item })
            await downloadItemAsync(item, settings.AutoImport, "status.preparingmanual");
    }

    private async void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RecommendationItem item })
            return;
        if (ReferenceEquals(preparedNextItem, item))
            preparedNextItem = null;
        recommendations.Remove(item);
        await refillPoolAsync();
        setStatus("status.skipped");
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
                setStatus("status.nonext");
                return;
            }

            MiniTrackDetailText.Text = Loc.T("status.minibuilding");
            // downloadItemAsync 内部已经把失败原因写进状态栏，这里只需要判断是否真的导入成功。
            if (!await downloadItemAsync(next, importAfterDownload: true, "status.installing"))
                return;

            if (previous is not null && !ReferenceEquals(previous, next))
                recommendations.Remove(previous);
            if (ReferenceEquals(preparedNextItem, next))
                preparedNextItem = null;
            await refillPoolAsync();
            setStatus("status.nextimported", next.DisplayTitle);
        }
        catch (OperationCanceledException)
        {
            setStatus("status.nextcancelled");
        }
        catch (Exception exception)
        {
            AppLog.Error("迷你播放器安装下一首失败", exception);
            setStatus("status.nextfail", conciseMessage(exception));
        }
        finally
        {
            miniNextBusy = false;
            MiniNextButton.IsEnabled = true;
            MiniNextButton.Content = Loc.T("btn.next");
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

    /// <summary>
    /// 导入发起后弹出谱面信息窗（贴在主窗口/迷你播放器下方）。
    /// 数据来自刚解析好的 <see cref="RecommendationItem.Detail"/> 和键型库。
    /// </summary>
    private void showMapInfoFor(RecommendationItem item, bool manual = false)
    {
        // manual = 用户自己点「i」：这时不受设置里「导入后自动弹出」的开关限制。
        if (!manual && !settings.ShowMapInfo)
            return;

        try
        {
            if (mapInfoWindow is null)
            {
                mapInfoWindow = new MapInfoWindow();
                mapInfoWindow.Closed += (_, _) => mapInfoWindow = null;
                mapInfoWindow.DockTo(this);
            }

            mapInfoWindow.ShowFor(item);
        }
        catch (Exception exception)
        {
            AppLog.Error("显示谱面信息窗失败", exception);
        }
    }

    /// <summary>迷你播放器里的「i」：信息栏没开就打开，已经开着就关掉。</summary>
    private void MiniInfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (mapInfoWindow?.IsVisible == true)
        {
            dismissMapInfo();
            return;
        }

        RecommendationItem? target = settings.MapInfoSource == MapInfoSources.InGame
            ? ingameInfoItem ?? currentRoamItem
            : currentRoamItem;
        if (target is null)
        {
            setStatus("status.nothingimported");
            return;
        }

        showMapInfoFor(target, manual: true);
    }

    /// <summary>长按 × 才退出：650ms 内松手就取消，进度环弹回。</summary>
    private Storyboard? closeHold;

    private void CloseButton_HoldStart(object sender, MouseButtonEventArgs e)
    {
        if (closeHold is not null)
            return;

        DoubleAnimation fill = new(0, 1, TimeSpan.FromMilliseconds(650)) { FillBehavior = FillBehavior.HoldEnd };
        Storyboard.SetTarget(fill, CloseHoldRing);
        Storyboard.SetTargetProperty(fill, new PropertyPath(Controls.ProgressRing.ProgressProperty));
        Storyboard board = new();
        board.Children.Add(fill);
        board.Completed += (_, _) =>
        {
            closeHold = null;
            Close();
        };
        closeHold = board;
        board.Begin();
    }

    private void CloseButton_HoldEnd(object sender, MouseButtonEventArgs e) => cancelCloseHold();

    private void CloseButton_HoldCancel(object sender, MouseEventArgs e) => cancelCloseHold();

    private void cancelCloseHold()
    {
        if (closeHold is null)
            return;
        closeHold.Stop();
        closeHold = null;
        CloseHoldRing.BeginAnimation(Controls.ProgressRing.ProgressProperty, null);
        CloseHoldRing.Progress = 0;
    }

    /// <summary>收起信息窗（开局 / 关掉设置选项 / 用户点 ×）。</summary>
    private void dismissMapInfo() => mapInfoWindow?.Dismiss();

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
        // 历史面板里同名 tag 的高亮也要跟着变，否则两个界面会看起来不一致。
        historyWindow?.RefreshEntries(archiveStore.Entries);
        e.Handled = true;
    }

    /// <summary>当前谱面身份，用来判定这次刷新是「换歌」还是「换难度」。</summary>
    private MapIdentity miniIdentity = MapIdentity.Empty;

    private void refreshMiniPlayer()
    {
        MapIdentity next = currentRoamItem is null
            ? MapIdentity.Empty
            : MapIdentity.From(
                currentRoamItem.BeatmapId,
                currentRoamItem.BeatmapsetId,
                currentRoamItem.Artist,
                currentRoamItem.Title);
        ChangeKind kind = ChangeKindDetector.ForEntrance(miniIdentity, next);
        miniIdentity = next;

        MiniPlayerHeader.DataContext = currentRoamItem;
        MiniTitleText.Text = currentRoamItem?.DisplayTitle ?? Loc.T("mini.nobeatmap");
        MiniTrackDetailText.Text = currentRoamItem is null
            ? Loc.T("mini.waiting")
            : $"{currentRoamItem.DifficultyName} · {currentRoamItem.DifficultySummary}";

        animateMiniPlayerEntrance(kind);
    }

    /// <summary>
    /// 迷你播放器的相位入场：换歌重放整块（淡入 + 上移 + 轻微放大），
    /// 换难度只让副标题纵向轻弹一次。参数取自 MMA 的 song-enter / diff-swap。
    /// </summary>
    private void animateMiniPlayerEntrance(ChangeKind kind)
    {
        if (!SystemParameters.ClientAreaAnimation || !IsLoaded)
            return;

        try
        {
            switch (kind)
            {
                case ChangeKind.Song:
                    startMiniAnimation(MiniPlayerHeader, UIElement.OpacityProperty, 0, 1, 420);
                    startMiniAnimation(MiniHeaderSlide, TranslateTransform.YProperty, 12, 0, 420);
                    startMiniAnimation(MiniHeaderScale, ScaleTransform.ScaleXProperty, 0.985, 1, 420);
                    startMiniAnimation(MiniHeaderScale, ScaleTransform.ScaleYProperty, 0.985, 1, 420);
                    startMiniAnimation(MiniTrackDetailText, UIElement.OpacityProperty, 0, 1, 360);
                    startMiniAnimation(MiniDetailSlide, TranslateTransform.XProperty, 22, 0, 360);
                    break;

                case ChangeKind.Difficulty:
                    startMiniAnimation(MiniTrackDetailText, UIElement.OpacityProperty, 0.4, 1, 260);
                    startMiniAnimation(MiniDetailSlide, TranslateTransform.YProperty, 6, 0, 260);
                    break;
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("迷你播放器入场动画失败", exception);
        }
    }

    /// <summary>
    /// 卡片入场。动画对象必须在运行时新建：模板里声明的 TranslateTransform 会被
    /// FrameworkTemplate 冻结，冻结后不能再挂动画（会抛 XamlParseException）。
    /// </summary>
    private void RecommendationCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement card)
            Controls.Motion.PlayEnter(card);
    }

    /// <summary>从 from 走到 to；结束后回到属性基值（Stop），不会把动画值留在属性上。</summary>
    private static void startMiniAnimation(IAnimatable target, DependencyProperty property, double from, double to, double milliseconds)
    {
        target.BeginAnimation(property, null);
        target.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private async Task handlePlayStartedAsync(TosuSnapshot snapshot)
    {
        // 谱面信息窗只在「导入 → 进游戏」这段里有用，真正开局就收起来。
        dismissMapInfo();

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
        recordArchive(playingItem);
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
        setStatus("status.result", played.DisplayTitle);
        try
        {
            // 结算完成时间比「开始游玩」更有意义，历史里按这个时间排。
            recordArchive(played);
            recommendations.Remove(played);
            RecommendationItem? next = preparedNextItem is not null && recommendations.Contains(preparedNextItem)
                ? preparedNextItem
                : recommendations.FirstOrDefault();
            if (next is not null && settings.AutoImport)
            {
                // 缺文件就先补下载再导入；失败时不要写成「已导入」，那条消息会误导排查。
                bool imported = await downloadItemAsync(next, importAfterDownload: true, "status.installingafter");
                if (imported)
                    setStatus("status.nextimported", next.DisplayTitle);
                else
                    setStatus("status.nextnotimported", next.DisplayTitle);
            }
            else if (next is null)
            {
                setStatus("status.resultnone");
            }

            await refillPoolAsync();
            playingItem = null;
            preparedNextItem = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            AppLog.Error("成功结算后的自动导入失败", exception);
            setStatus("status.autofail", conciseMessage(exception));
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
        lastSnapshot = snapshot;
        ConnectionText.Text = connectionTextFor(snapshot);
        updatePlayVisibility(snapshot);
    }

    /// <summary>
    /// 游玩中把 Roam 整个收起来（主窗口、迷你播放器、信息栏、历史面板），
    /// 结算 / 中途退出 / 暂停再恢复。
    ///
    /// 关键在「什么算在游玩」：加载阶段 tosu 报的是 state=play 且 paused=true
    /// （playTime 还没开始走），按 paused 判断就会在加载期间把窗口放出来、
    /// 等真正开局再收起——于是信息栏早关了、迷你播放器还在，回来只剩迷你播放器。
    /// 所以用 time.live 区分：live==0 是加载，算游玩中；live&gt;0 且 paused 才是真暂停。
    /// </summary>
    private void updatePlayVisibility(TosuSnapshot snapshot)
    {
        bool inPlay = IsInPlayState(snapshot);
        if (inPlay == hiddenForPlay)
            return;

        hiddenForPlay = inPlay;
        if (inPlay)
        {
            rememberLayout();
            restoreHistoryAfterPlay = historyWindow?.IsVisible == true;
            dismissMapInfo();
            historyWindow?.Hide();
            fadeOutThenHide();
            return;
        }

        showRestoringLayout();
    }

    /// <summary>
    /// 判断「现在该不该把 Roam 藏起来」。拆出来是为了能单独测：
    /// 加载阶段 tosu 报 play + paused（时间还没走），必须算作游玩中，
    /// 否则窗口会在加载时先弹回来、进入游戏才收起，造成两边不同步。
    /// </summary>
    internal static bool IsInPlayState(TosuSnapshot snapshot)
    {
        if (!string.Equals(snapshot.StateName, "play", StringComparison.OrdinalIgnoreCase))
            return false;
        return !(snapshot.IsPaused && snapshot.LiveTime > 0);
    }

    private bool layoutWasCollapsed;
    private double layoutWidth;
    private double layoutHeight;
    private double layoutLeft;
    private double layoutTop;
    private int playVisibilityToken;

    /// <summary>收起前把当前布局记下来：展开/迷你、尺寸、位置。</summary>
    private void rememberLayout()
    {
        layoutWasCollapsed = isCollapsed;
        layoutWidth = Width;
        layoutHeight = isCollapsed ? expandedHeight : Height;
        layoutLeft = Left;
        layoutTop = Top;
    }

    /// <summary>淡出 + 轻微下沉后收窗口；信息窗退场用的也是这一套，不会残留一帧。</summary>
    private void fadeOutThenHide()
    {
        int token = ++playVisibilityToken;
        if (!SystemParameters.ClientAreaAnimation)
        {
            RootCard.Opacity = 1;
            rootSlide.Y = 0;
            Hide();
            return;
        }

        try
        {
            DoubleAnimation fade = Controls.Motion.tween(1, 0, 150);
            fade.FillBehavior = FillBehavior.HoldEnd;
            fade.Completed += (_, _) =>
            {
                // 期间又被叫醒了就别藏了
                if (token != playVisibilityToken)
                    return;
                RootCard.BeginAnimation(UIElement.OpacityProperty, null);
                RootCard.Opacity = 1;
                rootSlide.BeginAnimation(TranslateTransform.YProperty, null);
                rootSlide.Y = 0;
                Hide();
            };
            RootCard.BeginAnimation(UIElement.OpacityProperty, fade);
            rootSlide.BeginAnimation(TranslateTransform.YProperty, Controls.Motion.tween(0, 8, 150));
        }
        catch (Exception exception)
        {
            AppLog.Error("游玩时隐藏窗口失败", exception);
            RootCard.BeginAnimation(UIElement.OpacityProperty, null);
            RootCard.Opacity = 1;
            rootSlide.BeginAnimation(TranslateTransform.YProperty, null);
            rootSlide.Y = 0;
            Hide();
        }
    }

    /// <summary>恢复：先按「进游戏前」的布局摆好，再淡入上浮。</summary>
    private void showRestoringLayout()
    {
        int token = ++playVisibilityToken;
        RootCard.BeginAnimation(UIElement.OpacityProperty, null);
        rootSlide.BeginAnimation(TranslateTransform.YProperty, null);
        RootCard.Opacity = SystemParameters.ClientAreaAnimation ? 0 : 1;
        rootSlide.Y = SystemParameters.ClientAreaAnimation ? 8 : 0;

        Show();

        // 布局按记录恢复。这里必须用「无动画」的强制布局：
        // CollapseButton_Click 带 190ms 高度动画，中途被打断会让 isCollapsed 和
        // 实际可见性不一致，那正是「回来只剩迷你播放器」的来源。
        forceLayout(layoutWasCollapsed, layoutWidth, layoutHeight);

        if (!double.IsNaN(layoutLeft)) Left = layoutLeft;
        if (!double.IsNaN(layoutTop)) Top = layoutTop;

        if (!SystemParameters.ClientAreaAnimation || token != playVisibilityToken)
            return;

        try
        {
            DoubleAnimation fade = Controls.Motion.tween(0, 1, 170);
            fade.FillBehavior = FillBehavior.HoldEnd;
            fade.Completed += (_, _) =>
            {
                RootCard.BeginAnimation(UIElement.OpacityProperty, null);
                RootCard.Opacity = 1;
                rootSlide.BeginAnimation(TranslateTransform.YProperty, null);
                rootSlide.Y = 0;
            };
            RootCard.BeginAnimation(UIElement.OpacityProperty, fade);
            rootSlide.BeginAnimation(TranslateTransform.YProperty, Controls.Motion.tween(8, 0, 170));
        }
        catch (Exception exception)
        {
            AppLog.Error("游玩回来恢复窗口失败", exception);
            RootCard.BeginAnimation(UIElement.OpacityProperty, null);
            RootCard.Opacity = 1;
            rootSlide.BeginAnimation(TranslateTransform.YProperty, null);
            rootSlide.Y = 0;
        }

        if (restoreHistoryAfterPlay && historyWindow is not null)
            historyWindow.Show();
        restoreHistoryAfterPlay = false;
    }

    /// <summary>
    /// 不走动画，直接把窗口切到指定布局。收/放两边都要用到它
    /// （按钮那条路径有动画，恢复布局时不能复用）。
    /// </summary>
    private void forceLayout(bool collapsed, double keepWidth, double keepExpandedHeight)
    {
        isCollapsed = collapsed;
        MainContent.BeginAnimation(HeightProperty, null);
        MainContent.ClearValue(HeightProperty);

        if (collapsed)
        {
            MainContent.Visibility = Visibility.Collapsed;
            Footer.Visibility = Visibility.Collapsed;
            NormalHeader.Visibility = Visibility.Collapsed;
            MiniPlayerHeader.Visibility = Visibility.Visible;
            MiniBackdrop.Visibility = Visibility.Visible;
            MinHeight = 64;
            MinWidth = 390;
            ResizeMode = ResizeMode.NoResize;
            Height = 64;
            Width = Math.Max(390, keepWidth);
            CollapseButton.Content = "+";
            CollapseButton.ToolTip = Loc.T("tip.expand");
            refreshMiniPlayer();
            return;
        }

        MainContent.Visibility = Visibility.Visible;
        MainContent.Opacity = 1;
        Footer.Visibility = Visibility.Visible;
        NormalHeader.Visibility = Visibility.Visible;
        MiniPlayerHeader.Visibility = Visibility.Collapsed;
        MiniBackdrop.Visibility = Visibility.Collapsed;
        MinHeight = 300;
        MinWidth = 390;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Height = Math.Max(300, keepExpandedHeight);
        Width = Math.Max(390, keepWidth);
        CollapseButton.Content = "−";
        CollapseButton.ToolTip = Loc.T("tip.miniplayer");
    }

    private static string connectionTextFor(TosuSnapshot snapshot) => Loc.T(snapshot.StateName switch
    {
        "play" when snapshot.IsPaused => "tosu.paused",
        "play" when snapshot.Failed => "tosu.failed",
        "play" => "tosu.playing",
        "resultScreen" => "tosu.result",
        "selectPlay" => "tosu.select",
        _ => "tosu.connected"
    });

    // ================= 状态栏 / 语言 =================

    /// <summary>按 key 写状态栏，同时把 key 记下来，换语言时能原地重画。</summary>
    private void setStatus(string key, params object?[] args)
    {
        statusKey = key;
        statusArgs = args;
        statusRaw = null;
        StatusText.Text = Loc.F(key, args);
    }

    /// <summary>已经是成品文字（异常信息之类）的状态，只能原样记住。</summary>
    private void setStatusText(string text)
    {
        statusKey = null;
        statusArgs = null;
        statusRaw = text;
        StatusText.Text = text;
    }

    private void renderStatus()
    {
        if (statusKey is not null)
            StatusText.Text = Loc.F(statusKey, statusArgs ?? []);
        else if (statusRaw is not null)
            StatusText.Text = statusRaw;
        else
            StatusText.Text = Loc.T("status.ready");
    }

    /// <summary>
    /// 语言换了。XAML 里的 {DynamicResource L.*} 由 WPF 自己刷新，
    /// 这里只补上代码里拼的那些：状态栏、迷你播放器、连接状态、卡片详情。
    /// </summary>
    private void applyLanguage()
    {
        MiniTitleText.Text = currentRoamItem?.DisplayTitle ?? Loc.T("mini.nobeatmap");
        if (profile is null)
            ProfileText.Text = Loc.T("profile.waiting");
        StartButton.Content = Loc.T(isRunning ? "btn.stop" : "btn.start");
        MiniNextButton.Content = miniNextBusy ? "..." : Loc.T("btn.next");
        if (miniNextBusy)
            MiniTrackDetailText.Text = Loc.T("status.minibuilding");
        else
            MiniTrackDetailText.Text = currentRoamItem is null
                ? Loc.T("mini.waiting")
                : $"{currentRoamItem.DifficultyName} · {currentRoamItem.DifficultySummary}";
        ConnectionText.Text = lastSnapshot is null ? Loc.T("conn.idle") : connectionTextFor(lastSnapshot);
        renderStatus();
        foreach (RecommendationItem item in recommendations)
            item.NotifyLanguage();
        foreach (SimpleFilterOption option in statusFilterOptions) option.NotifyLanguage();
        foreach (SimpleFilterOption option in genreFilterOptions) option.NotifyLanguage();
        foreach (SimpleFilterOption option in languageFilterOptions) option.NotifyLanguage();
        historyWindow?.ApplyLanguage();
    }

    private async void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!settingsControlsReady)
            return;

        string tag = (LanguageBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? Loc.AutoTag;
        if (tag == Loc.ToTag(Loc.Setting))
            return;

        settings.Language = tag;
        Loc.Apply(tag);
        try
        {
            await settingsStore.SaveAsync(settings);
        }
        catch (Exception exception)
        {
            AppLog.Error("保存语言设置失败", exception);
        }
    }

    private async Task stopAsync()
    {
        sessionCancellation.Cancel();
        await tosu.StopAsync();
        isRunning = false;
        StartButton.Content = Loc.T("btn.start");
        setStatus("status.stopped");
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
            ConnectionText.Text = Loc.T("tosu.startfail");
            setStatus("status.tosufail", conciseMessage(exception));
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
        value.CacheLimitMegabytes = parseInt(CacheLimitBox, 128, 65536);
        value.ExcludeRecentlyPlayed = ExcludeRecentCheck.IsChecked == true;
        value.ExcludeInstalledBeatmaps = ExcludeInstalledCheck.IsChecked == true;
        value.AutoDownload = AutoDownloadCheck.IsChecked == true;
        value.AutoImport = AutoImportCheck.IsChecked == true;
        value.UseEmbeddedFont = EmbeddedFontCheck.IsChecked == true;
        value.ShowMapInfo = MapInfoCheck.IsChecked == true;
        value.MapInfoSource = (MapInfoSourceBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? MapInfoSources.Imported;
        value.Language = (LanguageBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? Loc.AutoTag;
        value.OsuClient = (OsuClientBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "lazer";
        value.OsuExecutablePath = BeatmapDownloadService.NormalizeExecutablePath(OsuPathBox.Text);
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
        UsernameBox.Text = settings.Username;        ClientIdBox.Text = settings.ClientId;
        ClientSecretBox.Password = settingsStore.ReadSecret(settings);
        MinimumYearBox.Text = settings.MinimumYear.ToString();
        MaximumYearBox.Text = settings.MaximumYear.ToString();
        MinimumLengthBox.Text = settings.MinimumLengthSeconds.ToString();
        MaximumLengthBox.Text = settings.MaximumLengthSeconds.ToString();
        ExplorationSlider.Value = settings.Exploration;
        ExcludeRecentCheck.IsChecked = settings.ExcludeRecentlyPlayed;
        ExcludeInstalledCheck.IsChecked = settings.ExcludeInstalledBeatmaps;
        AutoDownloadCheck.IsChecked = settings.AutoDownload;
        AutoImportCheck.IsChecked = settings.AutoImport;
        EmbeddedFontCheck.IsChecked = settings.UseEmbeddedFont;
        MapInfoCheck.IsChecked = settings.ShowMapInfo;
        foreach (ComboBoxItem item in MapInfoSourceBox.Items)
            if (Equals(item.Tag?.ToString(), settings.MapInfoSource)) MapInfoSourceBox.SelectedItem = item;
        tosuFiles.Configure(settings.TosuWebSocketUrl);
        applyFontSetting();
        OsuPathBox.Text = BeatmapDownloadService.NormalizeExecutablePath(settings.OsuExecutablePath);
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
        CacheLimitBox.Text = settings.CacheLimitMegabytes.ToString();
        foreach (ComboBoxItem item in LanguageBox.Items)
            if (Equals(item.Tag?.ToString(), Loc.ToTag(Loc.Parse(settings.Language)))) LanguageBox.SelectedItem = item;
        // 配置里的语言在这里才生效（App 启动时用的是「跟随系统」）。
        Loc.Apply(settings.Language);
        applySimpleFilterControls();
        updateDifficultyControls();
        updateExplorationText();
        settingsControlsReady = true;
    }

    /// <summary>
    /// 应用字体设置。Outfit 是编进程序集的资源，<c>UiFont</c> 是 DynamicResource，
    /// 所以在这里换资源能立刻刷新已经建好的界面，不用重启。
    /// </summary>
    private void applyFontSetting()
    {
        Typography.EnsureUsable();
        Typography.Apply(settings.UseEmbeddedFont);
        AppLog.Info(Typography.Describe());
    }

    /// <summary>
    /// --experiment=ingame：不用连 WebSocket，直接把 tosu 的 /json/v2 喂给同一条解析链路，
    /// 再看「信息栏显示游戏内选中的谱面」能拼出什么。验证用。
    /// </summary>
    private async Task<string> runInGameProbeAsync()
    {
        tosuFiles.Configure(settings.TosuWebSocketUrl);
        string ws = settings.TosuWebSocketUrl;
        if (!Uri.TryCreate(ws, UriKind.Absolute, out Uri? uri))
            return $"WebSocket 地址不合法：{ws}";
        string http = (uri.Scheme == "wss" ? "https" : "http") + $"://{uri.Host}:{uri.Port}";

        using System.Net.Http.HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
        string json;
        try
        {
            json = await client.GetStringAsync($"{http}/json/v2");
        }
        catch (Exception exception)
        {
            return $"连不上 tosu（{http}）：{exception.Message}";
        }

        TosuSnapshot? snapshot = null;
        await using (TosuClient probe = new())
        {
            probe.SnapshotReceived += value => snapshot = value;
            probe.ProcessSnapshotJson(json);
        }
        if (snapshot is null)
            return "tosu 返回的 JSON 解析不出快照";

        StringBuilder report = new();
        report.AppendLine($"state={snapshot.StateName} id={snapshot.BeatmapId} set={snapshot.BeatmapsetId}");
        report.AppendLine($"artist={snapshot.Artist}");
        report.AppendLine($"title={snapshot.Title}");
        report.AppendLine($"version={snapshot.DifficultyName}");
        report.AppendLine($"mapper={snapshot.Mapper}");
        report.AppendLine($"stars={snapshot.Stars:0.00} length={snapshot.LengthSeconds:0}s");

        RecommendationItem? item = await buildItemFromTosuAsync(snapshot);
        if (item is null)
        {
            report.AppendLine("拿不到 .osu（tosu 可能没在选歌界面，或 /files/beatmap/file 不可用）");
            return report.ToString();
        }

        report.AppendLine($"mode={item.Detail?.ModeDisplay} keys={item.KeyCount} " +
                          $"CS/OD/AR/HP={item.Detail?.CircleSize:0.0}/{item.Detail?.OverallDifficulty:0.0}/{item.Detail?.ApproachRate:0.0}/{item.Detail?.HPDrainRate:0.0}");
        report.AppendLine($"bpm={item.Bpm:0} 物件={item.Detail?.ObjectCount}");
        report.AppendLine($"键型={(item.Pattern is null ? "（非 mania 或分析失败）" : item.Pattern.Category)}");
        if (item.Pattern is { } pattern)
        {
            report.AppendLine("占比：" + string.Join("，", pattern.CoreShares
                .OrderByDescending(pair => pair.Value)
                .Select(pair => $"{pair.Key} {pair.Value:P0}")));
            report.AppendLine("雷达：" + string.Join(" ", pattern.SkillAxes.Select(value => value.ToString("0.00"))));
        }
        return report.ToString();
    }

    /// <summary>后台扫本地已安装谱面；失败只记日志，去重退回「历史 + 近期玩过」。</summary>
    private static async Task refreshInstalledLibraryAsync()
    {
        try
        {
            await LocalBeatmapLibrary.RefreshAsync(StoragePaths.Root, CancellationToken.None);
            if (LocalBeatmapLibrary.Note is { } note)
                AppLog.Info(note);
        }
        catch (Exception exception)
        {
            AppLog.Error("扫描已安装谱面失败", exception);
        }
    }

    private async void ExcludeInstalledCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!settingsControlsReady)
            return;
        settings.ExcludeInstalledBeatmaps = ExcludeInstalledCheck.IsChecked == true;
        try
        {
            await settingsStore.SaveAsync(settings);
        }
        catch (Exception exception)
        {
            AppLog.Error("保存已安装谱面去重设置失败", exception);
        }

        if (settings.ExcludeInstalledBeatmaps && !LocalBeatmapLibrary.HasData)
            await refreshInstalledLibraryAsync();
    }

    private async void MapInfoSourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!settingsControlsReady)
            return;

        string source = (MapInfoSourceBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? MapInfoSources.Imported;
        if (source == settings.MapInfoSource)
            return;

        settings.MapInfoSource = source;
        ingameInfoKey = null;
        dismissMapInfo();
        try
        {
            await settingsStore.SaveAsync(settings);
        }
        catch (Exception exception)
        {
            AppLog.Error("保存信息栏来源设置失败", exception);
        }
    }

    /// <summary>
    /// 「信息栏显示游戏内选中的谱面」：选歌界面里换一首就刷新一次。
    /// 数据全来自 tosu——元数据在快照里，键型从 `/files/beatmap/file` 取 `.osu` 现算。
    /// 开局 / 结算 / 回菜单都不在这里处理，交给原有的「开局自动关闭」那套逻辑。
    /// </summary>
    private async void updateInGameInfo(TosuSnapshot snapshot)
    {
        if (settings.MapInfoSource != MapInfoSources.InGame || !settings.ShowMapInfo)
            return;
        if (snapshot.BeatmapId <= 0 || !string.Equals(snapshot.StateName, "selectPlay", StringComparison.OrdinalIgnoreCase))
            return;

        string key = $"{snapshot.BeatmapId}|{snapshot.DifficultyName}";
        if (string.Equals(key, ingameInfoKey, StringComparison.Ordinal))
            return;
        ingameInfoKey = key;

        RecommendationItem? item = await buildItemFromTosuAsync(snapshot);
        if (item is not null)
        {
            ingameInfoItem = item;
            showMapInfoFor(item);
        }
    }

    /// <summary>把 tosu 的当前谱面拼成一个和推荐卡片同构的条目，信息窗不用区分数据来源。</summary>
    private async Task<RecommendationItem?> buildItemFromTosuAsync(TosuSnapshot snapshot)
    {
        string? osuText = await tosuFiles.TryGetBeatmapFileAsync(snapshot.BeatmapId, CancellationToken.None);
        if (osuText is null)
        {
            AppLog.Info($"信息栏：拿不到谱面 {snapshot.BeatmapId} 的 .osu");
            return null;
        }

        BeatmapDetail? detail = BeatmapDetailReader.Parse(osuText);
        ManiaPattern? pattern = settings.Mode == "mania" ? ManiaPatternAnalyzer.Analyze(osuText) : null;

        RecommendationItem item = new()
        {
            BeatmapId = snapshot.BeatmapId,
            BeatmapsetId = snapshot.BeatmapsetId,
            Artist = snapshot.Artist,
            Title = snapshot.Title,
            DifficultyName = snapshot.DifficultyName,
            Mapper = snapshot.Mapper,
            Stars = snapshot.Stars,
            Bpm = detail?.Bpm ?? 0,
            LengthSeconds = (int)Math.Round(snapshot.LengthSeconds),
            RankedYear = 0,
            Score = 0,
            KeyCount = detail?.KeyCount ?? 0
        };
        item.ApplyDetail(detail);
        item.ApplyPattern(pattern);
        // 曲绘走的是 Roam 自己的 CoverLoader（和推荐卡片同一套模糊底），
        // 不加载的话信息窗就只剩纯色背景。
        item.LoadCover();
        return item;
    }

    private async void MapInfoCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!settingsControlsReady)
            return;
        settings.ShowMapInfo = MapInfoCheck.IsChecked == true;
        if (!settings.ShowMapInfo)
            dismissMapInfo();
        try
        {
            await settingsStore.SaveAsync(settings);
        }
        catch (Exception exception)
        {
            AppLog.Error("保存谱面信息窗设置失败", exception);
        }
    }

    private async void EmbeddedFontCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!settingsControlsReady)
            return;
        settings.UseEmbeddedFont = EmbeddedFontCheck.IsChecked == true;
        applyFontSetting();
        try
        {
            await settingsStore.SaveAsync(settings);
        }
        catch (Exception exception)
        {
            AppLog.Error("保存字体设置失败", exception);
        }
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
            setStatus("status.suggesting");
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
                setStatus("status.suggested");
            }
            else setStatus("status.suggestnone");
        }
        catch (Exception ex) { setStatusText(conciseMessage(ex)); }
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
        // 历史面板里的同名 tag 也要一起跟着变亮/变暗，否则取消一个已选 tag 后
        // 推荐池的高亮消失了、历史面板还亮着。
        historyWindow?.RefreshEntries(archiveStore.Entries);
    }

    /// <summary>tag 池远程更新成功：重建池子（含白名单顺序）并刷新界面。</summary>
    private void onTagCatalogChanged() => Dispatcher.BeginInvoke(() =>
    {
        if (settings is null)
            return;
        settings.NormalizeTags();
        refreshTagList();
        AppLog.Info($"tag 池已更新：{TagCatalog.Pool.Count} 条（来源 {TagCatalog.Source}）");
    });

    private static async Task refreshTagCatalogAsync()
    {
        bool updated = await TagCatalog.RefreshAsync();
        if (!updated)
            AppLog.Info($"tag 池沿用本地：{TagCatalog.Pool.Count} 条（来源 {TagCatalog.Source}）");
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
            if (historyWindow is { IsVisible: true })
            {
                historyWasVisible = true;
                historyWindow.Hide();
            }
            expandedHeight = Height;
            expandedWidth = Width;
            MainContent.Visibility = Visibility.Collapsed;
            Footer.Visibility = Visibility.Collapsed;
            NormalHeader.Visibility = Visibility.Collapsed;
            MiniPlayerHeader.Visibility = Visibility.Visible;
            MiniBackdrop.Visibility = Visibility.Visible;
            refreshMiniPlayer();
            MinHeight = 64;
            MinWidth = 390;
            animateWindowHeight(64, 64, 64);
            Width = Math.Max(390, expandedWidth);
            ResizeMode = ResizeMode.NoResize;
            CollapseButton.Content = "+";
            CollapseButton.ToolTip = Loc.T("tip.expand");
        }
        else
        {
            // 展开时主内容树先不挂出来：卡片列表每帧重排一次就是掉帧来源
            // （实测内容可见时 max 77ms，隐藏后降到 ~37ms）。
            // 窗口长完再让内容以很短的淡入显形，避免逐帧布局。
            double target = Math.Max(300, expandedHeight);
            MainContent.Visibility = Visibility.Collapsed;
            Footer.Visibility = Visibility.Visible;
            NormalHeader.Visibility = Visibility.Visible;
            MiniPlayerHeader.Visibility = Visibility.Collapsed;
            MiniBackdrop.Visibility = Visibility.Collapsed;
            MainContent.ClearValue(HeightProperty);
            if (historyWasVisible && historyWindow is not null)
            {
                historyWasVisible = false;
                historyWindow.PrepareEntrance();
                historyWindow.Show();
                historyWindow.PlayEntrance();
            }
            ResizeMode = ResizeMode.CanResizeWithGrip;
            MinWidth = 390;
            // 展开过程中先把 MinHeight 压住，否则 64→300 会被 MinHeight 立刻夹到 300。
            animateWindowHeight(target, 64, 300, revealMainContent);
            Width = Math.Max(390, expandedWidth);
            CollapseButton.Content = "−";
            CollapseButton.ToolTip = Loc.T("tip.miniplayer");
        }
    }

    /// <summary>
    /// 展开动画结束后才把主内容挂出来：先以 0 透明度显示完成一次布局，
    /// 下一帧再淡入，避免"出现的那一刻"正好撞在窗口 resize 的帧上。
    /// </summary>
    private void revealMainContent()
    {
        MainContent.Opacity = SystemParameters.ClientAreaAnimation ? 0 : 1;
        MainContent.Visibility = Visibility.Visible;
        if (!SystemParameters.ClientAreaAnimation)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                DoubleAnimation fade = Controls.Motion.tween(0, 1, 120);
                fade.FillBehavior = FillBehavior.HoldEnd;
                fade.Completed += (_, _) =>
                {
                    MainContent.BeginAnimation(UIElement.OpacityProperty, null);
                    MainContent.Opacity = 1;
                };
                MainContent.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            catch (Exception exception)
            {
                AppLog.Error("主内容淡入失败", exception);
                MainContent.BeginAnimation(UIElement.OpacityProperty, null);
                MainContent.Opacity = 1;
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// 折叠/展开时的高度过渡。
    /// 注意不能用「基值先设终点 + Stop」：那样基值一改窗口立刻跳到终点，下一帧
    /// 动画再把它拉回起点，肉眼就是抽搐一下。这里基值保持不动，动画用 HoldEnd
    /// 持有终点，结束后再回写基值。
    /// </summary>
    private void animateWindowHeight(double target, double minHeightDuring, double minHeightAfter,
        Action? completed = null, double milliseconds = 160)
    {
        double from = ActualHeight > 0 ? ActualHeight : Height;
        MinHeight = minHeightDuring;

        if (!SystemParameters.ClientAreaAnimation || double.IsNaN(from) || Math.Abs(from - target) < 0.5)
        {
            BeginAnimation(HeightProperty, null);
            Height = target;
            MinHeight = minHeightAfter;
            completed?.Invoke();
            return;
        }

        try
        {
            DoubleAnimation animation = new(from, target, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            animation.Completed += (_, _) =>
            {
                BeginAnimation(HeightProperty, null);
                Height = target;
                MinHeight = minHeightAfter;
                completed?.Invoke();
            };
            BeginAnimation(HeightProperty, null);
            BeginAnimation(HeightProperty, animation);
        }
        catch (Exception exception)
        {
            AppLog.Error("窗口高度动画失败", exception);
            BeginAnimation(HeightProperty, null);
            Height = target;
            MinHeight = minHeightAfter;
            completed?.Invoke();
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
