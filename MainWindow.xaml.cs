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
    /// <summary>当前信息窗显示的是哪条 tosu 提示（避免同一句反复弹）。</summary>
    private string? tosuNoticeKey;
    /// <summary>「tosu 读不到 osu!」的顶栏提示只报一次。</summary>
    private bool tosuUnreadableNoticeShown;
    private RecommendationItem? ingameInfoItem;
    /// <summary>窗口整体的淡入淡出位移，运行时创建（模板里的 Freezable 会被冻结）。</summary>
    private readonly TranslateTransform rootSlide = new();
    private readonly TopmostKeeper topmostKeeper;
    private readonly ObservableCollection<RecommendationItem> recommendations = [];
    private readonly HashSet<int> sessionImportedBeatmapsetIds = [];
    private readonly BeatmapDownloadService downloader;
    private readonly PatternLibrary patternLibrary = new();
    private readonly OsuFileFetcher osuFileFetcher = new();

    /// <summary>键型分析串行化：它已移到线程池执行，避免多个下载并发写同一份键型库。</summary>
    private readonly SemaphoreSlim patternGate = new(1, 1);
    private readonly ArchiveStore archiveStore = new();
    private MapInfoWindow? mapInfoWindow;
    private PreviewWindow? previewWindow;
    /// <summary>谱面预览的「当前是哪一首」：id|难度名，换歌才重新取 .osu。</summary>
    private string previewKey = string.Empty;
    private ManiaPreviewChart? previewChart;
    private readonly Dictionary<int, ManiaPreviewChart> previewCharts = [];
    private HistoryWindow? historyWindow;
    private bool historyWasVisible;
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
    /// <summary>
    /// 推荐池的代次：每建一次池 +1。后台的键型验证发现代次变了就停手，
    /// 免得把上一轮的验证结果贴到新池子上。
    /// </summary>
    private int poolGeneration;
    /// <summary>
    /// 关键字搜索召回、但 tag 白名单没命中的谱面集。它们的 tag 不可信（可能只是曲名/作者撞词），
    /// 决断权交给解析出来的真实键型。
    /// </summary>
    private readonly HashSet<int> keywordBeatmapsetIds = [];

    /// <summary>
    /// 搜索到的全部谱面集（包括还没判键型的）。勾了键型时它们只是「待判」，
    /// 判定通过才进 candidates；没勾键型时直接就是候选。
    /// </summary>
    private List<OsuBeatmapset> searchResults = [];

    /// <summary>键型判定通过、可以进候选的谱面集。</summary>
    private readonly HashSet<int> admittedSetIds = [];

    /// <summary>键型不达标的谱面集：严格池不用，候选不足时才拿来兜底。</summary>
    private readonly HashSet<int> patternMismatchSetIds = [];

    /// <summary>准入 worker 是否在跑（避免每批候选都叠一个）。</summary>
    private bool admitting;

    /// <summary>上一次真正重排的时间，用于合并高频请求。</summary>
    private DateTime lastPoolPublishUtc = DateTime.MinValue;
    /// <summary>已经排了一次延迟重排，别重复排。</summary>
    private bool poolPublishScheduled;
    /// <summary>上次重排时的输入指纹：候选数、键型库版本、池内顺序。</summary>
    private int publishedCandidateCount = -1;
    private int publishedLibraryRevision = -1;
    private string publishedPoolSignature = string.Empty;

    /// <summary>
    /// 进行中的「池内预下载」，按谱面集 id 索引。池子每次重排都会重新调度：
    /// 出池的取消、新进池的插队，同一个谱面集不会下两次。
    /// </summary>
    private readonly Dictionary<int, PoolDownload> poolDownloads = [];

    /// <summary>一张正在进行的池内下载：取消源 + 任务本体（其它路径可以 await 它）。</summary>
    private sealed record PoolDownload(CancellationTokenSource Cancellation, Task Task);
    /// <summary>已经失败过一次的谱面集，避免失败即重试的死循环（每张只补一次）。</summary>
    private readonly HashSet<int> poolDownloadRetried = [];
    /// <summary>本轮排序的随机种子：同一轮内重排结果可复现，不同轮之间才有变化。</summary>
    private int recommendationSeed;
    /// <summary>
    /// 推荐池重排的最小间隔。验证/翻页时每来一批都会请求重排，这里合并成 1.5 秒一次，
    /// 否则长跑时池子会一直抖（用户说的「抽搐」）。
    /// </summary>
    private static readonly TimeSpan PoolPublishInterval = TimeSpan.FromMilliseconds(1500);

    /// <summary>准入最多跑几轮（每轮最多 AdmissionBudgetPerRound 次抓取）。</summary>
    private const int AdmissionRounds = 3;
    /// <summary>准入每轮的抓取预算。</summary>
    private const int AdmissionBudgetPerRound = 12;
    /// <summary>准入每轮的截止秒数。</summary>
    private const int AdmissionSecondsPerRound = 8;
    /// <summary>流式发布间隔：验证过程中每隔这么久重排一次池子。</summary>
    private static readonly TimeSpan StreamingPublishInterval = TimeSpan.FromMilliseconds(400);
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
    private readonly System.Windows.Threading.DispatcherTimer tosuHealthTimer;
    /// <summary>tosu 连续连不上几次了；每 4 次试着重拉一次。</summary>
    private int tosuConnectFailures;
    private int tosuRestartAttempts;
    /// <summary>预览收到的「无效状态」从什么时候开始（用于容忍切歌瞬间的抖动）。</summary>
    private DateTime? previewInvalidSince;
    /// <summary>tosu 连着却读不到游戏状态时的自愈计时（现场：僵尸 tosu 占着端口，连得上但没数据）。</summary>
    private DateTime emptyStateSince;
    private DateTime lastTosuRestartAt;
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
        downloader.Notice += message => Dispatcher.BeginInvoke(() => setStatusText(message));
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
            tosuConnectFailures = 0;
            tosuRestartAttempts = 0;
            // 连着却一直报空状态 = tosu 读不到游戏（或者它已经坏在某个端口上）。
            if (string.IsNullOrWhiteSpace(snapshot.StateName))
            {
                if (emptyStateSince == default)
                    emptyStateSince = DateTime.UtcNow;
            }
            else
            {
                emptyStateSince = default;
            }
            updateGameState(snapshot);
            updateInGameInfo(snapshot);
            updatePreview(snapshot);
        });
        tosu.ConnectFailed += () => Dispatcher.BeginInvoke(noteTosuConnectFailure);
        tosu.PlayStarted += snapshot => Dispatcher.BeginInvoke(async () => await handlePlayStartedAsync(snapshot));
        tosu.PlaySucceeded += snapshot => Dispatcher.BeginInvoke(async () => await handlePlaySucceededAsync(snapshot));
        previewPlayer.PlaybackStopped += (_, _) =>
        {
            if (!stoppingPreview && previewItem is not null)
                Dispatcher.BeginInvoke(stopPreview);
        };
        Loaded += MainWindow_Loaded;
        tosuHealthTimer = new System.Windows.Threading.DispatcherTimer(TimeSpan.FromSeconds(5), System.Windows.Threading.DispatcherPriority.Background,
            (_, _) => checkTosuHealth(), Dispatcher);
        tosuHealthTimer.Start();
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
        // 开发用二分开关：
        //   --import-launch=classic  用 1.5.5 原来的方式把谱面交给 osu!（ShellExecute、不设工作目录、不自动重试）
        //   --no-startup-ws          打开程序时不连 tosu（1.5.5 是点了「开始漫游」才连）
        // 用来定位「发布包能导入、本地新构建一导入就让游戏崩」到底出在哪一步。
        // 默认沿用发布包的方式；--import-launch=direct 才切到实验性的那条。
        BeatmapDownloadService.ClassicLaunch = !Environment.GetCommandLineArgs().Any(argument =>
            argument.Equals("--import-launch=direct", StringComparison.OrdinalIgnoreCase));
        // --no-tosu：连 tosu 都不碰（不启动进程、不连 WebSocket），用来单独验证
        // 「导不进去」到底和 tosu 有没有关系。
        bool manageTosu = !Environment.GetCommandLineArgs().Any(argument =>
            argument.Equals("--no-tosu", StringComparison.OrdinalIgnoreCase));
        bool startupWebSocket = manageTosu && !Environment.GetCommandLineArgs().Any(argument =>
            argument.Equals("--no-startup-ws", StringComparison.OrdinalIgnoreCase));
        if (!manageTosu)
            AppLog.Info("--no-tosu：这一轮不启动、也不连接 tosu");
        // 默认逐帧直算（速度等于时钟速度）；--preview-retained 才用图块 + 合成器动画。
        Controls.PreviewOverlay.LegacyMode = !Environment.GetCommandLineArgs()
            .Any(a => a.Equals("--preview-retained", StringComparison.OrdinalIgnoreCase));
        // --preview-proto：保留旧原型开关；正式预览已默认使用合成器动画。
        if (Environment.GetCommandLineArgs().Any(a => a.Equals("--preview-proto", StringComparison.OrdinalIgnoreCase)))
        {
            Controls.PreviewOverlay.PrototypeMode = true;
            AppLog.Info("预览：合成器动画原型模式");
        }

        // --preview-pace=<0|1|2|3>：提交节拍模式，配合 PresentMon 做对照。
        string? paceArg = Environment.GetCommandLineArgs()
            .FirstOrDefault(argument => argument.StartsWith("--preview-pace=", StringComparison.OrdinalIgnoreCase));
        if (paceArg is not null && int.TryParse(paceArg["--preview-pace=".Length..], out int pace))
        {
            Controls.PreviewOverlay.PaceMode = Math.Clamp(pace, 0, 3);
            AppLog.Info($"预览提交节拍模式：{Controls.PreviewOverlay.PaceMode}");
        }

        // --gpu=<序号>：混合显卡笔记本上指定预览用哪块 GPU（见日志里的「可用适配器」）。
        string? gpuArg = Environment.GetCommandLineArgs()
            .FirstOrDefault(argument => argument.StartsWith("--gpu=", StringComparison.OrdinalIgnoreCase));
        if (gpuArg is not null && int.TryParse(gpuArg["--gpu=".Length..], out int gpuIndex))
        {
            Controls.PreviewOverlay.SkinDirectory = settings.PreviewSkinPath ?? string.Empty;
        Controls.PreviewOverlay.AdapterIndex = gpuIndex;
            AppLog.Info($"预览渲染指定适配器：--gpu={gpuIndex}");
        }
        if (BeatmapDownloadService.ClassicLaunch)
            AppLog.Info($"导入启动方式：{(BeatmapDownloadService.ClassicLaunch ? "classic（发布包的行为）" : "direct（实验性）")}");
        if (!startupWebSocket)
            AppLog.Info("启动时不连 tosu（1.5.5 的行为）");

        if (settings.AutoStartTosu && manageTosu)
            await ensureTosuReadyAsync(sessionCancellation.Token);

        // 谱面预览和「信息栏显示游戏内选中的谱面」都只靠 WebSocket 快照，
        // 所以打开 Roam 就连上，不要等到点了「开始漫游」才连 —— 否则用户在选歌
        // 界面等半天也没有预览（那正是这两个功能的主场）。
        if (startupWebSocket)
            tosu.Start(settings.TosuWebSocketUrl);

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

        // 开发用：--notice 截「tosu 没读到 osu!」的提示态（--notice=memory 换原因）。
        // 先正常显示一张卡片量到高度，再切提示态 —— 和真实顺序一致，能验证「高度锁定」。
        if (Environment.GetCommandLineArgs().FirstOrDefault(arg => arg.StartsWith("--notice", StringComparison.OrdinalIgnoreCase)) is { } noticeArg)
        {
            string reason = noticeArg.Contains('=') ? noticeArg[(noticeArg.IndexOf('=') + 1)..] : "memory";
            showMapInfoFor(buildSnapshotItem());
            await Task.Delay(900);
            if (mapInfoWindow is not null)
                AppLog.Info($"信息窗高度（谱面态）：{mapInfoWindow.ActualHeight:0.0}｜{mapInfoWindow.DescribeHeights()}");
            showTosuNotice(reason == "nobeatmap" ? "mapinfo.tosu.nobeatmap" : "mapinfo.tosu.memory");
            await Task.Delay(700);
            if (mapInfoWindow is not null)
            {
                AppLog.Info($"信息窗高度（提示态）：{mapInfoWindow.ActualHeight:0.0}｜{mapInfoWindow.DescribeHeights()}");
                RenderDiagnostics.CaptureWindow(mapInfoWindow, path);
            }
            Close();
            return;
        }

        // 开发用：--pending 截「键型还没算完」的占位态，验证高度和算完以后一致
        if (Environment.GetCommandLineArgs().Any(arg => arg == "--pending"))
        {
            RecommendationItem item = buildSnapshotItem();
            item.ApplyPattern(null);
            item.SetPatternPending(true);
            showMapInfoFor(item);
            await Task.Delay(900);
            if (mapInfoWindow is not null)
            {
                AppLog.Info($"信息窗高度（占位态）：{mapInfoWindow.ActualHeight:0.0}｜{mapInfoWindow.DescribeHeights()}");
                RenderDiagnostics.CaptureWindow(mapInfoWindow, path);
            }
            Close();
            return;
        }

        // 开发用：--preview 截谱面预览窗。先用缓存里第一张 .osz 摆出信息窗当高度参照，
        // 这样能一次看清「两个窗等高」和「预览画得对不对」。
        //   --preview=noclock        模拟 tosu 不给预览音频位置，验证那条提示
        //   --preview=layered        用分层窗口截一张（帧率对照用）
        //   --preview=fps            量帧率（默认的普通窗口）
        //   --preview=fps-layered    量帧率（分层窗口，对照）
        string? previewArgument = Environment.GetCommandLineArgs()
            .FirstOrDefault(arg => arg.StartsWith("--preview", StringComparison.OrdinalIgnoreCase));
        if (previewArgument is not null)
        {
            string mode = previewArgument.Contains('=')
                ? previewArgument[(previewArgument.IndexOf('=') + 1)..].Trim().ToLowerInvariant()
                : string.Empty;
            bool fpsProbe = mode is "fps" or "fps-layered" or "fps-pair" or "fps-floating";
            bool layeredWindow = mode is "layered" or "fps-layered";
            bool standalone = mode == "fps-tiny";

            ManiaPreviewChart? chart = null;
            string osuTitle = "24eeev0-$";
            string mapper = "Asymetrical";
            try
            {
                string? osz = Directory.Exists(settingsStore.CacheDirectory)
                    ? Directory.EnumerateFiles(settingsStore.CacheDirectory, "*.osz").FirstOrDefault()
                    : null;
                if (osz is not null && OszReader.TryReadBeatmapText(osz, 0, out string text, out _))
                {
                    chart = ManiaPreviewChart.Parse(text);
                    BeatmapDetail? detail = BeatmapDetailReader.Parse(text);
                    if (detail is not null)
                    {
                        osuTitle = detail.Title;
                        mapper = detail.Creator;
                    }
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("截图模式解析预览谱面失败", exception);
            }

            if (chart is null)
            {
                AppLog.Info("--preview：缓存里没有可用的 mania 谱面");
                Close();
                return;
            }

            // 预览是信息窗的子页面，探针也必须先有信息窗（否则连画面都不会出现）。
            showMapInfoFor(buildSnapshotItem());
            await Task.Delay(1000);

            previewWindow = new PreviewWindow(layered: layeredWindow);
            previewWindow.Closed += (_, _) => previewWindow = null;
            if (standalone)
            {
                // 探针：不挂主窗口、固定小尺寸，用来看帧时间跟像素数量有没有关系。
                previewWindow.Width = 160;
                previewWindow.Height = 160;
            }
            else
            {
                previewWindow.DockTo(this);
                previewWindow.Anchor = mapInfoWindow;
            }

            if (mode == "fps-floating")
                previewWindow.Topmost = false;

            previewWindow.ShowChart(chart, osuTitle, $"{chart.KeyCount}K · {mapper}");

            if (fpsProbe)
            {
                // 先让启动期的后台活干完（扫已安装谱面、拉 tag 池），否则量到的是那些任务的占用。
                await Task.Delay(5000);
                previewWindow.BeginFpsProbe();
                // 模拟 tosu：每 90ms 灌一条时间，持续 3 秒（窗口自己每帧外推补平）
                System.Diagnostics.Stopwatch feed = System.Diagnostics.Stopwatch.StartNew();
                double anchorTime = chart.FirstNoteTime + 20000;
                while (feed.ElapsedMilliseconds < 3000)
                {
                    previewWindow.SetTime(anchorTime + feed.Elapsed.TotalMilliseconds);
                    await Task.Delay(90);
                }

                AppLog.Info($"帧率探针（{mode}）：{previewWindow.DescribeFps()}");
                Close();
                return;
            }

            previewWindow.SetTime(mode == "noclock" ? 0 : chart.FirstNoteTime + 30000);
            // --preview-hold=<毫秒>：让开发用的预览窗多停一会儿，方便抓屏看渲染结果。
            int holdMs = mode == "noclock" ? 3400 : 1000;
            string? holdArg = Environment.GetCommandLineArgs()
                .FirstOrDefault(argument => argument.StartsWith("--preview-hold=", StringComparison.OrdinalIgnoreCase));
            if (holdArg is not null && int.TryParse(holdArg["--preview-hold=".Length..], out int parsedHold))
                holdMs = Math.Clamp(parsedHold, 200, 60000);
            // 持续喂时间：预览时钟在「收不到新样本」时是会冻结的（那是给 tosu 断流用的保护），
            // 所以开发模式必须像 tosu 一样每 16ms 喂一次，否则画面只有开头动一下然后定住。
            // 用后台定时器喂，而不是 UI 线程上的 await 循环：后者在 62Hz 上跟渲染抢 CPU，
            // 会把「提交间隔」本身推高（实测 4.17ms → 5.6ms），让 PresentMon 对照失去意义。
            System.Diagnostics.Stopwatch feedClock = System.Diagnostics.Stopwatch.StartNew();
            double feedAnchor = mode == "noclock" ? 0 : chart.FirstNoteTime + 30000;
            PreviewWindow feeder = previewWindow;
            using (System.Threading.Timer feedTimer = new(
                       _ => feeder.SetTime(feedAnchor + feedClock.Elapsed.TotalMilliseconds),
                       null, dueTime: 0, period: 4))
            {
                await Task.Delay(holdMs);
            }

            // --preview-shot=<png>：让渲染线程把当前帧从显存读回来，确认真的画对了。
            string? shotPath = Environment.GetCommandLineArgs()
                .FirstOrDefault(argument => argument.StartsWith("--preview-shot=", StringComparison.OrdinalIgnoreCase))?["--preview-shot=".Length..];
            if (shotPath is not null)
            {
                await Task.Delay(1200);
                previewWindow.CaptureSurface(shotPath);
                await Task.Delay(800);
            }

            AppLog.Info($"预览窗：{previewWindow.Describe()}");
            AppLog.Info($"信息窗高度：{mapInfoWindow?.ActualHeight:0.0}（预览窗 {previewWindow.ActualHeight:0.0}）");
            if (shotPath is null)
                RenderDiagnostics.CaptureWindow(mapInfoWindow, path);
            if (mapInfoWindow is not null)
                RenderDiagnostics.CaptureWindow(mapInfoWindow, Path.Combine(
                    Path.GetDirectoryName(path) ?? ".", $"{Path.GetFileNameWithoutExtension(path)}-info.png"));
            Close();
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
            searchResults = [];
            admittedSetIds.Clear();
            patternMismatchSetIds.Clear();
            recommendations.Clear();
            playingItem = null;
            preparedNextItem = null;
            // 新的池子：之前那一轮的验证任务作废，排序换一个种子。
            poolGeneration += 1;
            recommendationSeed = Random.Shared.Next();
            keywordBeatmapsetIds.Clear();
            refreshTagList();

            HashSet<int> candidateIds = [];
            bool roamStarted = false;
            Task? firstPreparation = null;
            await api.SearchCandidatesInBatchesAsync(
                settings,
                settings.MinimumStars,
                settings.MaximumStars,
                async (batch, fromKeyword) =>
                {
                    List<OsuBeatmapset> newSets = batch.Where(set => candidateIds.Add(set.Id)).ToList();
                    if (newSets.Count == 0)
                        return;

                    if (fromKeyword)
                        keywordBeatmapsetIds.UnionWith(newSets.Select(set => set.Id));
                    searchResults.AddRange(newSets);
                    rebuildCandidates();
                    settings.RecordTags(newSets.SelectMany(set => AppSettings.ParseTags(set.Tags)));
                    refreshTagList();
                    maybeSubmitTags();
                    // 勾了键型：先判键型再进候选（准入 worker 自己边判边发布）。
                    if (usesPatternGate())
                        startAdmission();
                    else
                        refillPool();

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
                        if (settings.AutoDownload && recommendations.Count > 0)
                            firstPreparation = prepareFirstItemAsync();
                        else
                            setStatus("status.loaded", recommendations.Count);
                    }
                    else if (roamStarted)
                    {
                        setStatus("status.updated", recommendations.Count);
                    }
                },
                sessionCancellation.Token,
                // 只要还有「补位」卡片就一直往后翻页：补位是兜底，不是答案。
                // 常规 8 页 400 条经常不够（tag 白名单 + 排除已安装），翻到上限为止。
                wantsMore: () => recommendations.Count(item => !item.IsFallback) < Math.Max(1, settings.PoolSize),
                maximumPages: OsuApiClient.ExtendedPageBudget,
                // 够格的候选已经够填满池子，就不用再翻关键字了。
                enoughStrictCandidates: () =>
                    recommendations.Count(item => !item.IsFallback) >= Math.Max(1, settings.PoolSize));

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

        // 勾了键型 / 只要面谱：只认真实解析的结果。没验证到的先当「未知」，
        // 等后台验证落地后重排 —— 拿 tag 推断当结论会让排序看起来有依据，其实是猜的。
        bool verifiedOnly = settings.SelectedPatterns.Count > 0 || settings.PreferLn;
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
                cache[beatmap.Id] = patternLibrary.Resolve(beatmap.Id, set.Id, tags, settings.ManiaKeys, verifiedOnly);
            }
        }
        patternLookup = id => cache.TryGetValue(id, out ManiaPattern? pattern) ? pattern : null;
    }

    /// <summary>
    /// 候选准入：**先判键型，再决定要不要进候选**。
    ///
    /// 流程：搜索结果先进 <see cref="searchResults"/>（全部待判），这里逐张抓 `.osu`、解析键型，
    /// 占比达标的进候选（`admittedSetIds`），不达标的记为备用（`patternMismatchSetIds`），
    /// 严格池不够时才会用备用。够格候选攒到 `PoolSize × 2` 就停手，不再烧预算。
    /// </summary>
    private void startAdmission()
    {
        if (!usesPatternGate() || admitting || searchResults.Count == 0)
            return;
        if (sessionCancellation.IsCancellationRequested)
            return;
        admitting = true;
        _ = admitPatternsAsync(poolGeneration);
    }

    private async Task admitPatternsAsync(int generation)
    {
        try
        {
            int wanted = Math.Max(1, settings.PoolSize) * 2;
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            for (int round = 0; round < AdmissionRounds; round += 1)
            {
                if (generation != poolGeneration || sessionCancellation.IsCancellationRequested)
                    return;
                if (admittedSetIds.Count >= wanted || searchResults.Count == 0)
                    return;
                int before = admittedSetIds.Count;
                Progress<(int Verified, int Hits)> progress = new(value =>
                {
                    if (generation == poolGeneration)
                        setStatus("status.admitting", value.Verified, admittedSetIds.Count, wanted);
                });
                PrefetchResult result = await PatternPrefetcher.RunAsync(
                    searchResults, settings, patternLibrary,
                    (beatmapId, token) => osuFileFetcher.TryFetchAsync(beatmapId, token),
                    AdmissionBudgetPerRound, TimeSpan.FromSeconds(AdmissionSecondsPerRound),
                    progress, sessionCancellation.Token, keywordBeatmapsetIds,
                    onIngested: admitJudgedChart, targetOverride: wanted);
                if (result.Parsed > 0)
                    await patternLibrary.SaveAsync();
                if (admittedSetIds.Count == before)
                {
                    AppLog.Info($"候选准入：第 {round + 1} 轮没捞到达标候选（解析 {result.Parsed}），停止判定");
                    return;
                }
            }
            clock.Stop();
            AppLog.Info($"候选准入结束：达标 {admittedSetIds.Count} 个谱面集，备用 {patternMismatchSetIds.Count} 个（{clock.ElapsedMilliseconds} ms）");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLog.Error("候选准入失败", exception);
        }
        finally
        {
            admitting = false;
            if (generation == poolGeneration)
            {
                await publishPoolAsync();
                if (admittedSetIds.Count == 0)
                {
                    AppLog.Info($"候选准入：判定 {patternMismatchSetIds.Count} 张都不达标，没有可用候选");
                    setStatus("status.admissionempty", patternMismatchSetIds.Count);
                }
                else if (settings.AutoDownload && playingItem is null && preparedNextItem is null &&
                         recommendations.Count > 0)
                {
                    // 准入比首曲预下载慢：这里补一次，否则池子填好了却没人开始下载。
                    _ = prepareFirstItemAsync();
                }
            }
        }
    }

    /// <summary>预取器每解析完一张就回调这里，跑在工作线程上，必须切回 UI 线程改状态。</summary>
    private void admitJudgedChart(int beatmapId, int beatmapsetId)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            ManiaPattern? pattern = patternLibrary.TryGetVerified(beatmapId);
            if (pattern is null || !searchResults.Any(set => set.Id == beatmapsetId))
                return;
            if (CandidateAdmission.IsStrict(pattern, settings))
            {
                admittedSetIds.Add(beatmapsetId);
                // tag 白名单没命中、但真实键型达标的：排序时按真实键型放行（rank 里的 keywordSourced 分支）。
                OsuBeatmapset? set = searchResults.FirstOrDefault(item => item.Id == beatmapsetId);
                if (set is not null && settings.IncludedTags.Count > 0)
                {
                    IReadOnlyList<string> tags = AppSettings.ParseTags(set.Tags);
                    if (!settings.IncludedTags.Any(tag => tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
                        keywordBeatmapsetIds.Add(beatmapsetId);
                }
            }
            else
            {
                patternMismatchSetIds.Add(beatmapsetId);
            }
            rebuildCandidates();
            requestPoolPublish();
        });
    }

    /// <summary>
    /// 首曲预下载：先等准入把第一批候选判出来（最多几秒），否则下的是注定被换掉的谱。
    /// </summary>
    private async Task prepareFirstItemAsync()
    {
        for (int wait = 0; wait < 15 && recommendations.Count == 0; wait += 1)
        {
            if (sessionCancellation.IsCancellationRequested)
                return;
            await Task.Delay(200);
        }

        if (recommendations.FirstOrDefault() is not { } first)
            return;

        schedulePoolDownloads();
        await downloadItemAsync(first, importAfterDownload: settings.AutoImport, "status.preparing");
    }


    /// <summary>
    /// 按「当前已知的键型」重排推荐池，并把结果和现有卡片对齐：
    /// 锁定中的卡片（正在玩 / 正在下载 / 已预约下一首）留在原位，其余按新排序替换。
    /// 验证过程中会反复调用，所以排序必须可复现 —— 随机源用本轮固定种子，
    /// 否则每次重排都会因为探索项抖动把卡片重新洗一遍。
    /// </summary>
    /// <summary>
    /// 请求一次重排：高频调用会被合并（1.5 秒一次），避免池子抽搐。
    /// 需要立刻拿到结果的场景（准入结束、漫游结束）直接用 <see cref="publishPoolAsync"/>。
    /// </summary>
    private void requestPoolPublish()
    {
        if (poolPublishScheduled || sessionCancellation.IsCancellationRequested)
            return;
        TimeSpan elapsed = DateTime.UtcNow - lastPoolPublishUtc;
        if (elapsed >= PoolPublishInterval)
        {
            _ = publishPoolAsync();
            return;
        }
        poolPublishScheduled = true;
        _ = publishAfterDelayAsync(PoolPublishInterval - elapsed);
    }

    private async Task publishAfterDelayAsync(TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, sessionCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            poolPublishScheduled = false;
            return;
        }
        poolPublishScheduled = false;
        await publishPoolAsync();
    }

    private string poolSignature() => string.Join(",", recommendations.Select(item => item.BeatmapsetId));

    private async Task publishPoolAsync()
    {
        // 候选集还没就绪（刚重建、第一批还没回来）时不能重排：空候选排序会算出空结果，
        // reconcilePool 会把池子里所有能换的卡片都换掉 —— 表现为「池子突然空了/闪一下」。
        if (profile is null || candidates.Count == 0)
            return;

        // 输入没变就不重排：候选数量、键型库版本、池内顺序三者一致时，结果必然一样。
        string signatureBefore = poolSignature();
        if (candidates.Count == publishedCandidateCount &&
            patternLibrary.Revision == publishedLibraryRevision &&
            signatureBefore == publishedPoolSignature)
            return;

        lastPoolPublishUtc = DateTime.UtcNow;
        refreshPatternCache();

        HashSet<int> inPool = [.. recommendations.Select(item => item.BeatmapsetId)];
        // 池里已有的谱面要重新参与排序（它们的分数刚被验证结果改过），所以要摘出「历史降权」，
        // 否则重排会把整个池子换成一批还没验证过的谱面。
        List<int> lowerPriority =
        [
            .. settings.RecommendationHistory.Where(id => !inPool.Contains(id)),
            .. sessionImportedBeatmapsetIds.Where(id => !inPool.Contains(id))
        ];
        if (settings.ExcludeRecentlyPlayed)
            lowerPriority.AddRange(profile.PlayedBeatmapsetIds().Where(id => !inPool.Contains(id)));

        // 严格条件先排；不够再用「放宽」补位（够格的谱面永远排在补位的前面，
        // 后续翻页/验证找到够格的就自然把它们替换掉）。
        // 严格段只用「键型达标」的候选；备用（键型不达标）留给下面补位。
        IReadOnlyList<RecommendationItem> desired = rankPool(
            lowerPriority, relaxed: false, excludeSetIds: usesPatternGate() ? patternMismatchSetIds : null);
        bool relaxedInstalled = false;
        if (desired.Count < Math.Max(1, settings.PoolSize))
        {
            IReadOnlyList<RecommendationItem> filler = rankPool(lowerPriority, relaxed: true, alreadyPicked: desired);
            if (filler.Count > 0)
                desired = [.. desired, .. filler];
        }
        // 一个都不剩、而「排除已安装谱面」正开着：这一步大概是元凶。
        // 与其给个空池子，不如放开这一条并明说 —— 已安装只是「下过」，不是「不喜欢」。
        if (desired.Count == 0 && settings.ExcludeInstalledBeatmaps && LocalBeatmapLibrary.HasData)
        {
            desired = rankPool(lowerPriority, relaxed: true, relaxInstalledExclusion: true);
            relaxedInstalled = desired.Count > 0;
        }

        bool historyChanged = reconcilePool(desired);
        updatePoolVisibility();
        int fallbackCount = recommendations.Count(item => item.IsFallback);
        // 池子空着但候选不为空：说明候选全被筛选条件挡掉了（最常见是「排除已安装谱面」）。
        // 这时候只说「没有候选谱面」等于把用户堵死，得把漏斗数字说出来。
        if (desired.Count == 0 && recommendations.Count == 0)
        {
            CandidateDiagnosis diagnosis = CandidateFilter.Diagnose(candidates, settings);
            AppLog.Info($"候选漏斗：{diagnosis.Sets} 个谱面集 → 可推荐难度 {diagnosis.EligibleDiffs} 个" +
                        $"（已装排除 {diagnosis.InstalledDiffs}，状态不符 {diagnosis.StatusMissedSets} 个集，tag 不符 {diagnosis.TagMissedSets} 个集）");
            setStatus("status.filtered", diagnosis.Sets, diagnosis.EligibleDiffs, diagnosis.InstalledDiffs);
        }
        else if (relaxedInstalled)
        {
            // 明确告诉用户：这一池里有已安装的谱面，是因为严格条件下一个都不剩。
            AppLog.Info($"已临时忽略「排除已安装谱面」：严格条件下池子为空，放开后 {desired.Count} 首");
            setStatus("status.relaxedinstalled", desired.Count);
        }
        else if (fallbackCount > 0)
        {
            // 不弹状态提示了：卡片自己带「放宽补位」标记，状态栏提示会被验证/下载状态顶掉，
            // 反而更混乱。日志留一条够排查。
            AppLog.Info($"补位卡 {fallbackCount} 首（tag 降级为加权 / 允许已玩过），后台继续找够格的替换");
        }
        else if (recommendations.Count > 0 && recommendations.All(item => item.IsRecycled))
        {
            setStatus("status.recycled");
        }
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
        publishedCandidateCount = candidates.Count;
        publishedLibraryRevision = patternLibrary.Revision;
        publishedPoolSignature = poolSignature();
        schedulePoolDownloads();
    }

    /// <summary>
    /// 按当前设置排一次池子。
    ///
    /// <paramref name="relaxed"/> = false 时是严格条件：白名单 tag 是硬筛、已推荐/已玩过的降权（通常直接排除）。
    /// = true 时是「补位」条件：白名单 tag 降级成加权（tagFit 仍然加分）、允许已玩过的谱面上来，
    /// 用来把凑不满的名额补上，并标记 <see cref="RecommendationItem.IsFallback"/> 以便后面替换。
    /// <paramref name="relaxInstalledExclusion"/> 进一步放开「排除已安装谱面」（只在严格条件一个都不剩时用）。
    /// </summary>
    private IReadOnlyList<RecommendationItem> rankPool(
        List<int> lowerPriority,
        bool relaxed,
        IReadOnlyList<RecommendationItem>? alreadyPicked = null,
        bool relaxInstalledExclusion = false,
        IReadOnlySet<int>? excludeSetIds = null)
    {
        int wanted = Math.Max(1, settings.PoolSize) - (alreadyPicked?.Count ?? 0);
        if (wanted <= 0)
            return [];
        HashSet<int> excluded = [.. alreadyPicked?.Select(item => item.BeatmapsetId) ?? []];
        if (excludeSetIds is not null)
            excluded.UnionWith(excludeSetIds);
        return recommendationEngine.RankAvoidingHistory(
            candidates, profile!, settings, settings.MinimumStars, settings.MaximumStars,
            excluded,
            // 补位时不再把「已推荐/已玩」当成排除项，否则候选不足时永远补不满。
            relaxed ? [] : lowerPriority,
            wanted,
            patternLookup, patternPreference, settings.PatternWeight,
            settings.ExcludeInstalledBeatmaps && !relaxInstalledExclusion && LocalBeatmapLibrary.HasData
                ? LocalBeatmapLibrary.InstalledBeatmapIds
                : null,
            new Random(recommendationSeed),
            relaxTagFilter: relaxed,
            isFallback: relaxed,
            keywordBeatmapsetIds: keywordBeatmapsetIds);
    }

    /// <summary>
    /// 把池子对齐到新的排序。返回是否有卡片新进池（需要记历史 / 存设置）。
    /// 已经加载过封面、展开过详情的同 id 卡片直接复用，避免重排时闪烁。
    /// </summary>

    private bool reconcilePool(IReadOnlyList<RecommendationItem> desired)
    {
        HashSet<int> pinned = pinnedBeatmapsetIds();
        List<RecommendationItem> next = PoolReconciler.Build(
            recommendations, desired, pinned, Math.Max(1, settings.PoolSize));
        if (PoolReconciler.SameSequence(recommendations, next))
            return false;

        HashSet<int> known = [.. recommendations.Select(item => item.BeatmapsetId)];
        // 原地对齐：整表 Clear 再重填会让列表闪烁、滚动位置归零，所以尽量只做 Move / Insert。
        for (int index = 0; index < next.Count; index += 1)
        {
            int existing = indexOfBeatmapset(next[index].BeatmapsetId, index);
            if (existing == index)
                continue;
            if (existing > index)
                recommendations.Move(existing, index);
            else
                recommendations.Insert(index, next[index]);
        }
        while (recommendations.Count > next.Count)
            recommendations.RemoveAt(recommendations.Count - 1);

        bool recorded = false;
        foreach (RecommendationItem item in next)
        {
            if (!known.Add(item.BeatmapsetId))
                continue;
            settings.RecordRecommendation(item.BeatmapsetId);
            item.LoadCover();
            recorded = true;
        }
        AppLog.Info($"推荐池重排：{recommendations.Count} 首（锁定 {pinned.Count}）");
        return recorded;
    }

    /// <summary>池子里这个谱面集现在的位置；从 from 开始往后找（前面已经对齐了）。</summary>
    private int indexOfBeatmapset(int beatmapsetId, int from)
    {
        for (int index = from; index < recommendations.Count; index += 1)
        {
            if (recommendations[index].BeatmapsetId == beatmapsetId)
                return index;
        }
        return -1;
    }

    /// <summary>这几张卡片不能被重排换掉：正在玩、正在下载 / 导入、已预约下一首。</summary>
    private HashSet<int> pinnedBeatmapsetIds()
    {
        HashSet<int> pinned = [];
        foreach (RecommendationItem? item in new[] { playingItem, preparedNextItem, currentRoamItem })
        {
            if (item is not null)
                pinned.Add(item.BeatmapsetId);
        }
        foreach (RecommendationItem item in recommendations)
        {
            if (item.DownloadState is DownloadState.Queued or DownloadState.Downloading or DownloadState.Importing)
                pinned.Add(item.BeatmapsetId);
        }
        // 正在下的一律锁住：中途换掉只会白下（取消路径会删半截文件，但带宽已经花掉了）。
        pinned.UnionWith(poolDownloads.Keys);
        return pinned;
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
    ///
    /// 这里不是「一次跑完的快照队列」，而是**可重排的调度**：池子每次重排都会调一次
    /// <see cref="schedulePoolDownloads"/>，出池的下载被取消、新进池的插到队首。
    /// 之前的实现是拍一个快照串行跑到底，验证结果一到池子换了，够格的卡片只能排在
    /// 已经没用的凑数卡片后面，表现为「所有下载都卡住」。
    /// </summary>
    private void schedulePoolDownloads()
    {
        if (!settings.AutoDownload || sessionCancellation.IsCancellationRequested)
        {
            cancelAllPoolDownloads();
            return;
        }

        List<RecommendationItem> desired = poolDownloadOrder();
        // 判据用「卡片还在不在池子里、还需不需要下」，**不能**用待下载清单：
        // 下载一启动状态就不是 Waiting 了，用清单判会把刚启动的下载立刻取消并无限重启。
        foreach ((int beatmapsetId, PoolDownload running) in poolDownloads.ToList())
        {
            if (!PoolDownloadPlanner.ShouldCancel(beatmapsetId, recommendations))
                continue;
            if (!poolDownloads.Remove(beatmapsetId))
                continue;
            running.Cancellation.Cancel();
            AppLog.Info($"预下载取消：set={beatmapsetId}（已出池或已下完）");
        }

        // 够格的候选在等空位，而占着空位的是「凑数」卡片：直接让它让位。
        // 这是原来最刺眼的现象 —— 验证结果一到，池子换了，但下载通道还被冷启动那几张占着。
        if (poolDownloads.Count >= BeatmapDownloadService.MaxConcurrentDownloads &&
            desired.Any(item => !item.IsFallback))
        {
            foreach ((int beatmapsetId, PoolDownload running) in poolDownloads.ToList())
            {
                RecommendationItem? runningItem = recommendations.FirstOrDefault(item => item.BeatmapsetId == beatmapsetId);
                if (runningItem is not { IsFallback: true })
                    continue;
                poolDownloads.Remove(beatmapsetId);
                // 让位过的补位卡不再自动排队，避免「让位→重排→又让位」的抖动。
                poolDownloadRetried.Add(beatmapsetId);
                running.Cancellation.Cancel();
                AppLog.Info($"预下载让位：set={beatmapsetId}（补位卡片，给够格候选腾通道）");
                break;
            }
        }

        // 还剩几个空位就给优先级最高的几张。
        foreach (RecommendationItem item in desired)
        {
            if (poolDownloads.Count >= BeatmapDownloadService.MaxConcurrentDownloads)
                break;
            if (poolDownloads.ContainsKey(item.BeatmapsetId))
                continue;
            CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellation.Token);
            Task task = runPoolDownloadAsync(item, cancellation);
            poolDownloads[item.BeatmapsetId] = new PoolDownload(cancellation, task);
        }
        reportPoolDownloadProgress();
    }

    /// <summary>补池子：请求一次重排（会被合并），需要的话启动准入。搜索结果每来一批都会调。</summary>
    private void refillPool()
    {
        if (profile is null)
            return;

        requestPoolPublish();
        if (usesPatternGate())
            startAdmission();
    }

    /// <summary>勾了键型 / 只要面谱时，候选必须先过键型判定。</summary>
    private bool usesPatternGate() =>
        settings.Mode == "mania" && (settings.SelectedPatterns.Count > 0 || settings.PreferLn);

    /// <summary>这张谱在「所选键型」上的占比；「只要面谱」时按是否 LN 谱算 1/0。</summary>
    private double PatternShare(ManiaPattern pattern) =>
        settings.SelectedPatterns.Count > 0
            ? pattern.SelectedShare(settings.SelectedPatterns)
            : pattern.IsLnChart ? 1 : 0;

    /// <summary>把候选列表刷新成「判定通过 + 备用」的那部分。</summary>
    private void rebuildCandidates()
    {
        if (!usesPatternGate())
        {
            candidates = searchResults;
            return;
        }
        candidates = [.. searchResults.Where(set => admittedSetIds.Contains(set.Id) || patternMismatchSetIds.Contains(set.Id))];
    }

    /// <summary>待下载顺序，规则见 <see cref="PoolDownloadPlanner"/>。</summary>
    private List<RecommendationItem> poolDownloadOrder() =>
        PoolDownloadPlanner.Order(recommendations, playingItem, preparedNextItem, poolDownloadRetried);

    private async Task runPoolDownloadAsync(RecommendationItem item, CancellationTokenSource cancellation)
    {
        try
        {
            await downloadItemAsync(item, importAfterDownload: false, "预下载", cancellation.Token,
                showStatus: false, ownedByScheduler: true);
            if (item.DownloadState == DownloadState.Failed)
                poolDownloadRetried.Add(item.BeatmapsetId);
        }
        catch (OperationCanceledException)
        {
            // 被取消是正常路径（卡片出池），不是错误。
        }
        catch (Exception exception)
        {
            poolDownloadRetried.Add(item.BeatmapsetId);
            AppLog.Error($"预下载失败：{item.DisplayTitle}", exception);
        }
        finally
        {
            if (poolDownloads.TryGetValue(item.BeatmapsetId, out PoolDownload? tracked) &&
                ReferenceEquals(tracked.Cancellation, cancellation))
                poolDownloads.Remove(item.BeatmapsetId);
            cancellation.Dispose();
            if (poolDownloads.Count == 0)
                trimBeatmapCache();
            reportPoolDownloadProgress();
            // 腾出空位后接着下下一张（不递归：排到 UI 队列下一拍）。
            _ = Dispatcher.InvokeAsync(schedulePoolDownloads);
        }
    }

    private void cancelAllPoolDownloads()
    {
        foreach (PoolDownload running in poolDownloads.Values)
            running.Cancellation.Cancel();
        poolDownloads.Clear();
    }

    private void reportPoolDownloadProgress()
    {
        int waiting = recommendations.Count(item => item.DownloadState is DownloadState.Waiting or DownloadState.Failed);
        int done = recommendations.Count(item => item.DownloadState is DownloadState.Downloaded or DownloadState.Imported or DownloadState.Importing);
        if (waiting == 0 && done > 0)
            setStatus("status.predownloaded", done);
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
    private async Task<bool> downloadItemAsync(
        RecommendationItem item,
        bool importAfterDownload,
        string statusKey,
        CancellationToken? cancellation = null,
        bool showStatus = true,
        bool ownedByScheduler = false)
    {
        CancellationToken token = cancellation ?? sessionCancellation.Token;
        // 这张卡正在被池内调度下载：等它下完，不要当成「已经就绪」——
        // 那会让导入撞上还没写完的文件（现场日志里的「预下载的谱面文件不存在」就是这条）。
        // ownedByScheduler 表示调用方就是那个调度任务本身，不能再等自己（会死锁）。
        if (!ownedByScheduler &&
            poolDownloads.TryGetValue(item.BeatmapsetId, out PoolDownload? inFlight))
        {
            if (showStatus)
                setStatus("status.withtitle", Loc.T(statusKey), item.DisplayTitle);
            try
            {
                await inFlight.Task;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch
            {
                // 失败原因由它自己的日志负责，这里继续按新状态走。
            }
        }
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
            if (showStatus)
                setStatus("status.withtitle", Loc.T(statusKey), item.DisplayTitle);
            string oszPath = Path.Combine(settingsStore.CacheDirectory, $"{item.BeatmapsetId}.osz");
            bool wantPattern = settings.Mode == "mania";

            // 只有「真的下完」且文件还在才算就绪。Downloading 不能跳过：
            // 那时文件还没写完，后面的导入会直接报「预下载的谱面文件不存在」。
            if (item.DownloadState != DownloadState.Downloaded ||
                !File.Exists(oszPath) || new FileInfo(oszPath).Length < 1024)
            {
                item.DownloadState = DownloadState.Queued;
                await downloader.DownloadAsync(item, settings, token);
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
                await downloader.ImportAsync(item, settings, token);
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

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RecommendationItem item })
            return;
        if (ReferenceEquals(preparedNextItem, item))
            preparedNextItem = null;
        recommendations.Remove(item);
        refillPool();
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
                refillPool();
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
            refillPool();
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
                ensureMapInfoWindow();

            mapInfoWindow!.ShowFor(item);
            tosuNoticeKey = null;
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
            // 用户主动点「i」：这时候必须给个交代，别只丢一句「还没有导入谱面」。
            if (settings.MapInfoSource == MapInfoSources.InGame)
            {
                showTosuNotice(tosuProcess.MemoryReadFailing ? "mapinfo.tosu.memory" : "mapinfo.tosu.nobeatmap", force: true);
                return;
            }

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

            refillPool();
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

        // tosu 连上了但读不到内存：顶栏换个说法，状态栏只提示一次。
        bool unreadable = tosuProcess.MemoryReadFailing;
        ConnectionText.Text = unreadable ? Loc.T("tosu.ready.unreadable") : connectionTextFor(snapshot);
        if (unreadable && !tosuUnreadableNoticeShown)
        {
            tosuUnreadableNoticeShown = true;
            setStatus("status.tosu.unreadable");
        }

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
            dismissPreview();
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

    /// <summary>
    /// tosu 掉线不会自己回来：启动时如果发现「已经有一个 tosu 在跑」就不归我们管，
    /// 那个进程之后死掉的话，Roam 只会一直重连、什么都不发生 —— 现场看到的就是
    /// 「tosu 不启动」。所以连续失败几次就再走一遍「确保 tosu 在跑」，最多三次，
    /// 收到快照（说明真的连上了）再清零。
    /// </summary>
    private void noteTosuConnectFailure()
    {
        if (!settings.AutoStartTosu)
            return;

        tosuConnectFailures++;
        if (tosuConnectFailures % 4 != 0 || tosuRestartAttempts >= 3)
            return;

        tosuRestartAttempts++;
        AppLog.Info($"tosu 连不上（连续 {tosuConnectFailures} 次），第 {tosuRestartAttempts} 次尝试重新拉起");
        _ = retryTosuAsync();
    }

    /// <summary>
    /// tosu 自愈：连着、但 20 秒都没报出任何游戏状态，而 osu! 明明在跑 ——
    /// 说明这个 tosu 已经读不到游戏了（现场就是僵尸 tosu 占着 24050）。
    /// 它是我们拉起来的才敢重启，两次重启之间至少隔 2 分钟。
    /// </summary>
    private async void checkTosuHealth()
    {
        try
        {
            if (!tosu.Connected || emptyStateSince == default)
                return;
            if ((DateTime.UtcNow - emptyStateSince).TotalSeconds < 20)
                return;
            if ((DateTime.UtcNow - lastTosuRestartAt).TotalSeconds < 120)
                return;
            if (!tosuProcess.IsManagedRunning || !isGameRunning())
                return;

            lastTosuRestartAt = DateTime.UtcNow;
            AppLog.Info("tosu 连着但 20 秒读不到游戏状态（osu! 在跑），重启 tosu");
            await tosuProcess.ShutdownAsync(stopManagedProcess: true);
            await Task.Delay(500);
            await ensureTosuReadyAsync(sessionCancellation.Token);
            emptyStateSince = default;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLog.Error("重启 tosu 失败", exception);
        }
    }

    private static bool isGameRunning()
    {
        try
        {
            return System.Diagnostics.Process.GetProcessesByName("osu!").Length > 0 ||
                   System.Diagnostics.Process.GetProcessesByName("osulazer").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task retryTosuAsync()
    {
        try
        {
            await ensureTosuReadyAsync(sessionCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLog.Error("重新拉起 tosu 失败", exception);
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
        value.PreviewScrollSeconds = parseDouble(PreviewScrollBox, 0.4, 4.0);
        value.PreviewLatencyMilliseconds = parseDouble(PreviewLatencyBox, 0, 300);
        value.PreviewSkinPath = PreviewSkinBox.Text.Trim();
        value.ExcludeRecentlyPlayed = ExcludeRecentCheck.IsChecked == true;
        value.ExcludeInstalledBeatmaps = ExcludeInstalledCheck.IsChecked == true;
        value.AutoDownload = AutoDownloadCheck.IsChecked == true;
        value.AutoImport = AutoImportCheck.IsChecked == true;
        value.UseEmbeddedFont = EmbeddedFontCheck.IsChecked == true;
        value.ShowMapInfo = MapInfoCheck.IsChecked == true;
        value.MapInfoSource = (MapInfoSourceBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? MapInfoSources.Imported;
        value.Language = (LanguageBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? Loc.AutoTag;
        value.OsuClient = OsuLocator.NormalizeClient((OsuClientBox.SelectedItem as ComboBoxItem)?.Tag?.ToString());
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
        PreviewCheck.IsChecked = settings.ShowPreview;
        TagSubmitCheck.IsChecked = settings.TagSubmitEnabled;
        foreach ((CheckBox? box, string name) in patternBoxes())
        {
            if (box is not null)
                box.IsChecked = settings.SelectedPatterns.Contains(name, StringComparer.OrdinalIgnoreCase);
        }
        PreferLnCheck.IsChecked = settings.PreferLn;
        foreach (ComboBoxItem item in MapInfoSourceBox.Items)
            if (Equals(item.Tag?.ToString(), settings.MapInfoSource)) MapInfoSourceBox.SelectedItem = item;
        tosuFiles.Configure(settings.TosuWebSocketUrl);
        applyFontSetting();
        OsuPathBox.Text = BeatmapDownloadService.NormalizeExecutablePath(settings.OsuExecutablePath);
        string osuClient = OsuLocator.NormalizeClient(settings.OsuClient);
        foreach (ComboBoxItem item in OsuClientBox.Items)
            if (Equals(item.Tag?.ToString(), osuClient)) OsuClientBox.SelectedItem = item;
        refreshOsuDetection();
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
        PreviewScrollBox.Text = settings.PreviewScrollSeconds.ToString("0.0#");
        PreviewLatencyBox.Text = settings.PreviewLatencyMilliseconds.ToString("0");
        PreviewSkinBox.Text = settings.PreviewSkinPath;
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

        bool selectPlay = string.Equals(snapshot.StateName, "selectPlay", StringComparison.OrdinalIgnoreCase);

        if (snapshot.BeatmapId <= 0)
        {
            // 选歌界面里明确有歌、tosu 却报不出 ID，才值得提示；
            // 主菜单同理但只在「已经知道 tosu 读不到内存」时提示一次，否则每次回菜单都弹。
            if (selectPlay || (tosuProcess.MemoryReadFailing &&
                               string.Equals(snapshot.StateName, "menu", StringComparison.OrdinalIgnoreCase)))
            {
                showTosuNotice(tosuProcess.MemoryReadFailing ? "mapinfo.tosu.memory" : "mapinfo.tosu.nobeatmap");
            }
            return;
        }

        if (!selectPlay)
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
            return;
        }

        // ID 有、.osu 取不到：tosu 那边活着但给不出文件。
        showTosuNotice("mapinfo.tosu.nofile");
    }

    /// <summary>
    /// tosu 没读到 osu! 时，信息窗直接说清楚，而不是什么都不显示。
    /// 同一个原因只弹一次（用户自己点「i」除外）；拿到真谱面后 <see cref="tosuNoticeKey"/> 会清空。
    /// </summary>
    private void showTosuNotice(string messageKey, bool force = false)
    {
        if (!force && string.Equals(messageKey, tosuNoticeKey, StringComparison.Ordinal))
            return;

        tosuNoticeKey = messageKey;
        AppLog.Info($"信息栏：tosu 没读到 osu!（{messageKey}）");
        try
        {
            if (mapInfoWindow is null)
                ensureMapInfoWindow();

            mapInfoWindow!.ShowNotice(messageKey);
        }
        catch (Exception exception)
        {
            AppLog.Error("显示 tosu 提示失败", exception);
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

        // 详情（CS/OD/AR/物件数）很便宜，直接算；键型 + 星数 + SV 要几十到几百毫秒，
        // 放到后台线程，窗口先出来再补图 —— 否则选歌时按下方向键会明显卡一下。
        BeatmapDetail? detail = await Task.Run(() => BeatmapDetailReader.Parse(osuText));

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
        // 曲绘走的是 Roam 自己的 CoverLoader（和推荐卡片同一套模糊底），
        // 不加载的话信息窗就只剩纯色背景。
        item.LoadCover();

        if (settings.Mode == "mania")
        {
            item.SetPatternPending(true);
            _ = analyzeTosuPatternAsync(item, osuText);
        }

        return item;
    }

    /// <summary>
    /// 键型（含星数与 SV）在后台算完再回填到同一个条目上，信息窗会自己刷新。
    /// 算过的谱面走键型库 —— 这就是「保存算过的 SV」：patterns.json 里连 SvMetrics 一起存，
    /// 换首歌再转回来不用重算。
    /// </summary>
    private async Task analyzeTosuPatternAsync(RecommendationItem item, string osuText)
    {
        ManiaPattern? cached = patternLibrary.TryGetVerified(item.BeatmapId);
        if (cached is not null)
        {
            item.SetPatternPending(false);
            item.ApplyPattern(cached);
            return;
        }

        try
        {
            await patternGate.WaitAsync();
            try
            {
                System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
                bool stored = await Task.Run(() => patternLibrary.Ingest(osuText, item.BeatmapId, item.BeatmapsetId));
                if (!stored)
                {
                    // 算不出来（键数/音符不够）也要把占位撤掉，否则卡片一直空着一块。
                    item.SetPatternPending(false);
                    return;
                }

                await patternLibrary.SaveAsync();
                if (clock.ElapsedMilliseconds > 150)
                    AppLog.Info($"信息栏键型解析较慢：{item.DisplayTitle}，{clock.ElapsedMilliseconds}ms（已在后台线程）");
            }
            finally
            {
                patternGate.Release();
            }
        }
        catch (Exception exception)
        {
            AppLog.Error($"信息栏：解析谱面键型失败 {item.BeatmapId}", exception);
            item.SetPatternPending(false);
            return;
        }

        item.SetPatternPending(false);
        ManiaPattern? pattern = patternLibrary.TryGet(item.BeatmapId);
        if (pattern is not null)
            item.ApplyPattern(pattern);
    }

    /// <summary>信息窗按需创建：谱面态和提示态共用，创建时挂好「跟着预览窗等高」的钩子。</summary>
    private MapInfoWindow ensureMapInfoWindow()
    {
        if (mapInfoWindow is not null)
            return mapInfoWindow;

        mapInfoWindow = new MapInfoWindow();
        mapInfoWindow.Closed += (_, _) =>
        {
            mapInfoWindow = null;
            if (previewWindow is not null)
                previewWindow.Anchor = null;
        };
        // 预览窗和信息窗等高：信息窗因为键型补图/换语言长高，这里跟着改；
        // 拖动时位置也要跟（LocationChanged），否则它会停在第一次算出来的地方。
        mapInfoWindow.SizeChanged += (_, _) => syncPreviewGeometry();
        mapInfoWindow.LocationChanged += (_, _) => syncPreviewGeometry();
        mapInfoWindow.IsVisibleChanged += (_, _) =>
        {
            // 信息窗被隐藏（或关闭）时，预览必须一起消失 —— 它是信息窗的子页面。
            if (mapInfoWindow?.IsVisible != true)
                previewWindow?.Dismiss();
            syncPreviewGeometry();
        };
        mapInfoWindow.DockTo(this);
        return mapInfoWindow;
    }

    /// <summary>把「信息窗现在多高、在哪」告诉预览窗——两者等高是设计要求。</summary>
    private void syncPreviewGeometry()
    {
        if (previewWindow is null)
            return;

        previewWindow.Anchor = mapInfoWindow;
        double height = mapInfoWindow?.IsVisible == true
            ? mapInfoWindow.ActualHeight
            : mapInfoWindow?.PreferredHeight ?? 0;
        if (height > 1)
            previewWindow.SetCardHeight(height);
    }

    /// <summary>
    /// 选歌界面的谱面预览：跟着 tosu 报的预览音频位置滚当前谱面。
    /// 只有换歌才重新取 <c>.osu</c>；同一首里只喂时间，别每帧都解析一遍。
    /// </summary>
    private void updatePreview(TosuSnapshot snapshot)
    {
        if (!settings.ShowPreview)
        {
            dismissPreview();
            return;
        }

        // tosu 在切歌时会短暂报出「状态不是 selectPlay」或者「谱面 id = 0」。
        // 以前只要遇到一条就立刻收起预览，于是切歌时看到「出现后立刻消失」。
        // 现在要求这种无效状态**连续持续 400ms** 才真的收起。
        bool selectPlay = string.Equals(snapshot.StateName, "selectPlay", StringComparison.OrdinalIgnoreCase);
        if (!selectPlay || snapshot.BeatmapId <= 0)
        {
            previewInvalidSince ??= DateTime.UtcNow;
            if ((DateTime.UtcNow - previewInvalidSince.Value).TotalMilliseconds > 400)
                dismissPreview();
            return;
        }

        previewInvalidSince = null;

        string key = $"{snapshot.BeatmapId}|{snapshot.DifficultyName}";
        if (!string.Equals(key, previewKey, StringComparison.Ordinal))
        {
            previewKey = key;
            previewChart = null;
            _ = loadPreviewChartAsync(snapshot, key);
            return;
        }

        if (previewChart is null)
            return;

        previewWindow?.SetTime(snapshot.PreviewTime);
    }

    /// <summary>取当前谱面的 <c>.osu</c> 并解析成预览用的物件表（后台线程，别卡选歌）。</summary>
    private async Task loadPreviewChartAsync(TosuSnapshot snapshot, string key)
    {
        try
        {
            await loadPreviewChartCoreAsync(snapshot, key);
        }
        catch (Exception exception)
        {
            AppLog.Error($"谱面预览：加载 {snapshot.BeatmapId} 失败", exception);
        }
    }

    private async Task loadPreviewChartCoreAsync(TosuSnapshot snapshot, string key)
    {
        int beatmapId = snapshot.BeatmapId;
        if (!previewCharts.TryGetValue(beatmapId, out ManiaPreviewChart? chart))
        {
            string? osuText = await tosuFiles.TryGetBeatmapFileAsync(beatmapId, CancellationToken.None);
            if (osuText is null)
            {
                // 选歌时按住方向键，tosu 的「当前谱面」可能还停在上一首：等一下再要一次。
                await Task.Delay(120);
                osuText = await tosuFiles.TryGetBeatmapFileAsync(beatmapId, CancellationToken.None);
            }

            if (!string.Equals(previewKey, key, StringComparison.Ordinal))
                return;

            if (osuText is null)
            {
                AppLog.Info($"谱面预览：拿不到 {beatmapId} 的 .osu");
                return;
            }

            chart = await Task.Run(() => ManiaPreviewChart.Parse(osuText));
            if (chart is null)
            {
                // 非 mania（或没有物件）：预览只对 mania 有意义，别的模式什么都不弹。
                AppLog.Info($"谱面预览：{beatmapId} 不是 mania 谱面，不显示");
                return;
            }

            if (previewCharts.Count > 32)
                previewCharts.Clear();
            previewCharts[beatmapId] = chart;
        }

        if (!string.Equals(previewKey, key, StringComparison.Ordinal))
            return;

        previewChart = chart;
        showPreviewFor(chart, snapshot);
    }

    private void showPreviewFor(ManiaPreviewChart chart, TosuSnapshot snapshot)
    {
        try
        {
            // 谱面预览是信息窗的一个子页面：信息窗不在就绝不显示。
            if (mapInfoWindow is null)
            {
                dismissPreview();
                return;
            }

            if (previewWindow is null)
            {
                previewWindow = new PreviewWindow();
                previewWindow.Closed += (_, _) => previewWindow = null;
                previewWindow.DockTo(this);
            }

            // 流速 / 延迟补偿：用户可调，改完立刻生效（见 PreviewTuning_LostFocus）。
            previewWindow.VisibleSeconds = settings.PreviewScrollSeconds;
            previewWindow.LatencyMilliseconds = settings.PreviewLatencyMilliseconds;
            syncPreviewGeometry();
            List<string> meta = [$"{chart.KeyCount}K"];
            if (!string.IsNullOrWhiteSpace(snapshot.Mapper))
                meta.Add(snapshot.Mapper);
            if (!string.IsNullOrWhiteSpace(snapshot.DifficultyName))
                meta.Add(snapshot.DifficultyName);
            previewWindow.ShowChart(chart, snapshot.Title, string.Join(" · ", meta));
            syncPreviewGeometry();
        }
        catch (Exception exception)
        {
            AppLog.Error("显示谱面预览失败", exception);
        }
    }

    /// <summary>收起预览窗，并把「当前是哪首」清掉：回到同一首时重新走一遍取谱面。</summary>
    private void dismissPreview()
    {
        previewWindow?.Dismiss();
        previewKey = string.Empty;
        previewChart = null;
    }

    /// <summary>勾选键型：多选，勾中的核心型用强权重参与排序（不硬筛）。</summary>
    private async void PatternCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!settingsControlsReady)
            return;
        settings.SelectedPatterns = selectedPatterns();
        settings.NormalizePatternSelection();
        try
        {
            await settingsStore.SaveAsync(settings);
        }
        catch (Exception exception)
        {
            AppLog.Error("保存键型偏好失败", exception);
        }
    }

    private List<string> selectedPatterns()
    {
        List<string> selected = [];
        foreach ((CheckBox? box, string name) in patternBoxes())
        {
            if (box?.IsChecked == true)
                selected.Add(name);
        }
        return selected;
    }

    private IEnumerable<(CheckBox? Box, string Name)> patternBoxes()
    {
        yield return (PatternStreamCheck, "Stream");
        yield return (PatternChordstreamCheck, "Chordstream");
        yield return (PatternJacksCheck, "Jacks");
        yield return (PatternCoordinationCheck, "Coordination");
        yield return (PatternDensityCheck, "Density");
        yield return (PatternMiscCheck, "Wildcard");
    }

    private async void PreferLnCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!settingsControlsReady)
            return;
        settings.PreferLn = PreferLnCheck.IsChecked == true;
        try
        {
            await settingsStore.SaveAsync(settings);
        }
        catch (Exception exception)
        {
            AppLog.Error("保存「只要面谱」设置失败", exception);
        }
    }

    private async void TagSubmitCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!settingsControlsReady)
            return;
        settings.TagSubmitEnabled = TagSubmitCheck.IsChecked == true;
        try
        {
            await settingsStore.SaveAsync(settings);
        }
        catch (Exception exception)
        {
            AppLog.Error("保存待审 tag 提交设置失败", exception);
        }
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

    /// <summary>
    /// 预览的两个手感参数（流速、延迟补偿）：失焦就立刻生效**并写盘**。
    /// 以前只改内存里的预览对象 —— 预览没开着时就完全没反应，重启又回到旧值，
    /// 所以用户会觉得「有时候有效有时候无效」。
    /// </summary>
    private async void PreviewTuning_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!settingsControlsReady)
            return;

        try
        {
            double seconds = parseDouble(PreviewScrollBox, 0.4, 4.0);
            double latency = parseDouble(PreviewLatencyBox, 0, 300);
            settings.PreviewScrollSeconds = seconds;
            settings.PreviewLatencyMilliseconds = latency;

            if (previewWindow is not null)
            {
                previewWindow.VisibleSeconds = seconds;
                previewWindow.LatencyMilliseconds = latency;
            }

            await settingsStore.SaveAsync(settings);
            AppLog.Info($"预览参数：流速 {seconds:0.0#} 秒/屏，延迟补偿 {latency:0}ms");
        }
        catch (Exception exception)
        {
            AppLog.Error("预览参数不合法", exception);
        }
    }

    private async void PreviewCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!settingsControlsReady)
            return;
        settings.ShowPreview = PreviewCheck.IsChecked == true;
        if (!settings.ShowPreview)
            dismissPreview();
        try
        {
            await settingsStore.SaveAsync(settings);
        }
        catch (Exception exception)
        {
            AppLog.Error("保存谱面预览设置失败", exception);
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

    /// <summary>攒够了就在后台提交一次待审 tag；失败只是不提交，不打扰用户。</summary>
    private async void maybeSubmitTags()
    {
        try
        {
            if (!TagSubmission.ShouldSubmit(settings, TagIntake.Count, DateTimeOffset.UtcNow))
                return;

            int before = TagIntake.Count;
            if (await TagSubmission.SubmitAsync(settings))
            {
                AppLog.Info($"待审 tag 已提交：{before} → {TagIntake.Count}");
                await settingsStore.SaveAsync(settings);
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("提交待审 tag 失败", exception);
        }
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

    private string osuClientDisplayName()
    {
        OsuInstallation? installation = OsuLocator.Detect(settings.OsuClient, settings.OsuExecutablePath);
        return installation?.ClientName ?? Loc.T("osu.unknown");
    }

    private void OsuClientBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!settingsControlsReady)
            return;
        refreshOsuDetection();
    }

    private void RedetectOsuButton_Click(object sender, RoutedEventArgs e)
    {
        OsuLocator.Invalidate();
        refreshOsuDetection(logToFile: true);
    }

    /// <summary>
    /// 把「检测到哪个 osu!」摆到界面上。导入失败最常见的原因就是这里什么都没检测到，
    /// 而旧版本界面上什么都不说 —— 用户只会看到「点了导入没反应」。
    /// </summary>
    private void refreshOsuDetection(bool logToFile = false)
    {
        string client = OsuLocator.NormalizeClient((OsuClientBox.SelectedItem as ComboBoxItem)?.Tag?.ToString());
        string configured = BeatmapDownloadService.NormalizeExecutablePath(OsuPathBox.Text);
        OsuInstallation? installation = OsuLocator.Detect(client, configured);

        if (installation is null)
        {
            OsuDetectText.Text = Loc.T("osu.notfound");
            OsuDetectText.Foreground = (Brush)FindResource("WarningBrush");
            if (logToFile)
                AppLog.Info("重新检测 osu!：没找到（设置 / 进程 / 注册表 / 常见目录都没命中）");
            return;
        }

        OsuDetectText.Text = Loc.F("osu.detected", installation.ClientName, installation.Executable);
        OsuDetectText.Foreground = (Brush)FindResource("MutedTextBrush");
        if (logToFile)
            AppLog.Info($"重新检测 osu!：{installation.ClientName} → {installation.Executable}（来源：{installation.Source}）");
    }

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
