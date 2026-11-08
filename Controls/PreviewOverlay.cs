using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using OsuRoam.Services;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Color4 = Vortice.Mathematics.Color4;
using D2DRect = Vortice.Mathematics.Rect;

namespace OsuRoam.Controls;

/// <summary>
/// 谱面预览的覆盖层：一块**自带窗口**的 D3D11 画面，除音符外全部透明。
///
/// 三条约束决定了这个结构：
/// <list type="number">
/// <item>要逐像素透明，WPF 只能用分层窗口（软件合成，帧率砍半），所以这里不用 WPF 画。</item>
/// <item>要用硬件合成又要透明，走 DirectComposition + 合成交换链（<c>CreateSwapChainForComposition</c>），
/// 窗口带 <c>WS_EX_NOREDIRECTIONBITMAP</c>，DWM 直接按 alpha 合成。</item>
/// <item>要和主线程解耦，所以整条链（设备、交换链、绘制、时钟外推）都在自己的渲染线程上。</item>
/// </list>
///
/// 生命周期由信息窗决定：它只是信息窗的一个子页面，信息窗没了它就没有
/// （<see cref="SetOwner"/> 设置 owner，owner 关闭时随系统一起销毁）。
/// </summary>
public sealed partial class PreviewOverlay : IDisposable
{
    private const string WindowClass = "OsuRoamPreviewOverlay";

    private readonly bool useOpaque = OpaqueMode;
    private readonly bool useHwndAlpha = TransparentHwndMode;
    private readonly bool useLegacy = LegacyMode;
    private readonly object gate = new();
    private readonly Stopwatch now = Stopwatch.StartNew();
    private readonly List<double> frames = [];
    private readonly List<double> waitSamples = [];
    private readonly List<double> drawSamples = [];
    private readonly List<double> presentSamples = [];
    private readonly List<double> contentSamples = [];
    private int contentBackwards;
    private readonly ManualResetEventSlim resizeSignal = new(false);

    private ManiaPreviewChart? chart;
    private PreviewClock clock = new();
    private string? captureRequest;
    private bool transparentFramePending;
    private double lastStatusLog;
    private bool hasContentOnScreen;
    private bool showing;

    private IntPtr window;
    private IntPtr owner;
    private int pixelWidth = 1;
    private int pixelHeight = 1;
    private int pixelLeft;
    private int pixelTop;

    private Thread? renderThread;
    private CancellationTokenSource? stopSource;

    public PreviewOverlay()
    {
        registerWindowClass();
        window = CreateWindowEx(
            (useHwndAlpha ? 0 : WindowExStyles.WS_EX_NOREDIRECTIONBITMAP) | WindowExStyles.WS_EX_TRANSPARENT |
            WindowExStyles.WS_EX_NOACTIVATE | WindowExStyles.WS_EX_TOOLWINDOW | WindowExStyles.WS_EX_TOPMOST,
            WindowClass, "osu! Roam preview", WindowStyles.WS_POPUP,
            pixelLeft, pixelTop, pixelWidth, pixelHeight,
            IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

        if (window == IntPtr.Zero)
            AppLog.Error($"预览覆盖层：创建窗口失败（错误码 {Marshal.GetLastWin32Error()}）");
        if (useHwndAlpha && window != IntPtr.Zero)
        {
            IntPtr region = CreateRectRgn(0, 0, -1, -1);
            try
            {
                BlurBehind blur = new() { Flags = 3, Enable = true, Region = region };
                int result = DwmEnableBlurBehindWindow(window, ref blur);
                if (result < 0) Marshal.ThrowExceptionForHR(result);
            }
            finally { DeleteObject(region); }
        }
    }

    /// <summary>一屏画面覆盖多少毫秒的谱面（越小越快）。</summary>
    public double VisibleSeconds { get; set; } = 1.25;

    /// <summary>延迟补偿（毫秒）：预览要追上游戏画面的节奏，往未来多看一点。</summary>
    public double LatencyMilliseconds { get; set; } = 35;

    /// <summary>透明度（1 = 完全不透明）。</summary>
    public double Opacity { get; set; } = 1;

    public bool IsVisible
    {
        get
        {
            lock (gate)
                return showing;
        }
    }

    /// <summary>最近几秒的帧间隔（毫秒），给帧率探针用。</summary>
    public IReadOnlyList<double> FrameIntervals
    {
        get
        {
            lock (gate)
                return [.. frames];
        }
    }

    public double LastDrawMilliseconds { get; private set; }

    /// <summary>
    /// 相邻两帧**画面内容时间**的差值（毫秒）。这是「内容侧是否匀速」的直接证据：
    /// 若它们都稳定在刷新周期附近且没有回退，抖动只可能来自显示侧（合成/上屏）。
    /// </summary>
    public (double[] Deltas, int Backwards) ContentCadence
    {
        get
        {
            lock (gate)
                return (contentSamples.ToArray(), contentBackwards);
        }
    }

    /// <summary>等待节拍 / 绘制 / Present 三段耗时（毫秒，已跳过预热帧）。</summary>
    public (double[] Wait, double[] Draw, double[] Present) PhaseSamples
    {
        get
        {
            lock (gate)
                return (waitSamples.ToArray(), drawSamples.ToArray(), presentSamples.ToArray());
        }
    }

    /// <summary>
    /// 提交/节拍模式（PresentMon 对照用）：
    /// 0 = 等帧延迟对象再提交（现状）；1 = 不等，直接 Present(1,0)；
    /// 2 = 帧延迟对象 + MaximumFrameLatency=2（让 DWM 多攒一帧，容忍相位误差）。
    /// </summary>
    public static int PaceMode { get; set; } = 0;

    /// <summary>
    /// 原型模式：不逐帧提交，内容交给 IDCompositionAnimation 匀速平移，应用线程只负责
    /// 铺一次内容。用来验证「合成器自己推进」是否解决画面一抽一抽。
    /// </summary>
    public static bool PrototypeMode { get; set; }

    /// <summary>预览皮肤目录（空 = 用随程序的内置贴图）。**不自动扫描 osu!stable。**</summary>
    public static string SkinDirectory { get; set; } = string.Empty;

    /// <summary>顶部淡入（按屏幕位置逐段算 alpha）。默认关掉：先确认它是不是抖动来源之一。</summary>
    public static bool FadeEnabled { get; set; }

    /// <summary>诊断：把整块画面填成品红，用来区分「画没画」和「显示链路通不通」。</summary>
    public static bool OpaqueSelfTest { get; set; }

    /// <summary>仅供对照诊断：保留旧的逐帧交换链路径。</summary>
    /// <summary>
    /// 默认走**逐帧直算**（每帧按时钟算位置，速度必然等于时钟速度）。
    /// 设为 true 走图块 + 合成器动画那条路（--preview-retained），它靠估算速度/加速度，
    /// 现场出现了「速度跟不上音乐 + 周期性抖动」。
    /// </summary>
    public static bool LegacyMode { get; set; } = true;
    /// <summary>诊断用：单次匀速动画，不接受音频校正。</summary>
    public static bool UncorrectedMotionProbe { get; set; }
    /// <summary>
    /// 不透明呈现（普通窗口 + hwnd 交换链）。
    /// **默认关闭**：这条路径目前只画出黑底、音符不显示（D2D 目标与 hwnd 交换链的
    /// 后缓冲索引没对上），而透明 + DComp 那条路是验证过能正常画的。
    /// 需要实验时加 --preview-opaque。
    /// </summary>
    public static bool OpaqueMode { get; set; }
    public static bool TransparentHwndMode { get; set; }
    public static bool NoteVisualMode { get; set; }

    public string RendererDescription { get; private set; } = "DComp retained tiles";
    public bool UsesRetainedRenderer { get; private set; } = true;
    public int TileUpdates { get; private set; }
    public int AnimationCommits { get; private set; }

    /// <summary>创建 D3D 设备用的适配器序号（-1 = 默认硬件适配器）。</summary>
    public static int AdapterIndex { get; set; } = -1;
    public int DrawnNotes { get; private set; }

    /// <summary>把预览挂到信息窗下面：owner 关闭时它跟着消失，也不会跑到 owner 后面去。</summary>
    public void SetOwner(IntPtr hwnd)
    {
        if (hwnd == owner || window == IntPtr.Zero)
            return;
        owner = hwnd;
        SetWindowLongPtr64(window, GwlpHwndParent, hwnd);
        // 设 owner 会把 TOPMOST 丢掉（窗口变成「归属」于一个非置顶窗口），
        // 之后它就会藏在浏览器后面 —— 明确重新置顶一次。
        SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0,
            SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOACTIVATE);
    }

    /// <summary>按**物理像素**摆位置（调用方从信息窗的 GetWindowRect 拿，不用管 DPI）。</summary>
    public void SetBounds(int left, int top, int width, int height)
    {
        if (window == IntPtr.Zero)
            return;

        left = Math.Max(-20000, left);
        top = Math.Max(-20000, top);
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        bool sizeChanged = width != pixelWidth || height != pixelHeight;
        if (!sizeChanged && left == pixelLeft && top == pixelTop)
            return;
        pixelLeft = left;
        pixelTop = top;
        pixelWidth = width;
        pixelHeight = height;

        SetWindowPos(window, IntPtr.Zero, left, top, width, height,
            SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE);
        if (sizeChanged)
            resizeSignal.Set();
    }

    public void Show(ManiaPreviewChart? value)
    {
        lock (gate)
        {
            chart = value;
            clock = new PreviewClock();
            showing = value is not null;
        }

        if (value is null)
        {
            hide();
            return;
        }

        if (window == IntPtr.Zero)
            return;

        // 原型/无锚点时给一个默认位置，否则窗口是 0x0，什么都看不到。
        if (PrototypeMode && useLegacy)
        {
            SetBounds(600, 150, 300, 420);
            AppLog.Info($"预览覆盖层（原型）：窗口 {pixelWidth}x{pixelHeight} @ {pixelLeft},{pixelTop}");
        }
        else if (pixelWidth <= 1 || pixelHeight <= 1)
        {
            SetBounds(900, 200, 300, 420);
        }

        ShowWindow(window, ShowWindowCommand.SW_SHOWNOACTIVATE);
        startRendering();   // 线程常驻：已经在跑就直接复用，不再重建 D3D/DComp
    }

    /// <summary>
    /// 收起画面：**不销毁渲染栈、也不隐藏窗口**，只把内容清空并提交一帧全透明。
    /// 之前这里是 ShowWindow(SW_HIDE) + 停线程 + Dispose 设备/交换链/视觉树，
    /// 切歌时反复「拆了重建」，重建期间窗口是空的 —— 看起来就是「出现后立刻消失」。
    /// </summary>
    public void Hide()
    {
        lock (gate)
        {
            showing = false;
            chart = null;
            transparentFramePending = true;
        }

        // 设备/交换链/线程都留着（重建代价太高），但**窗口本身必须真的隐藏** ——
        // 预览是信息窗的子页面，信息窗没了它就不能还在屏幕上。
        if (window != IntPtr.Zero)
            ShowWindow(window, ShowWindowCommand.SW_HIDE);
    }

    private void hide()
    {
        if (window != IntPtr.Zero)
            ShowWindow(window, ShowWindowCommand.SW_HIDE);
        stopRendering();
    }

    /// <summary>喂 tosu 报的预览音频位置（毫秒）。</summary>
    public void Sync(double milliseconds)
    {
        // tosu 在「预览音频没在播」时会报 0；照它同步会让时钟回到开头，
        // 而谱面音符在几十秒后 → 面板变成一块空的黑底。这种情况直接忽略。
        if (!double.IsFinite(milliseconds) || milliseconds <= 0)
            return;

        string? stats;
        lock (gate)
        {
            clock.Sync(milliseconds, now.Elapsed.TotalMilliseconds);
            stats = clock.DescribeSamples();
        }

        if (stats is not null)
            AppLog.Info($"预览时钟：{stats}");
    }

    public void ResetFrameStats()
    {
        lock (gate)
        {
            frames.Clear();
            waitSamples.Clear();
            drawSamples.Clear();
            presentSamples.Clear();
            contentSamples.Clear();
            contentBackwards = 0;
            lastContentTime = double.NaN;
            warmupFrames = WarmupFrames;
            TileUpdates = 0;
            AnimationCommits = 0;
        }
    }

    /// <summary>清空后先跳过这么多帧再采样（首帧要建资源，混进去会让 p99 失真）。</summary>
    private double lastContentTime = double.NaN;
    private int warmupFrames = WarmupFrames;
    private const int WarmupFrames = 90;

    /// <summary>
    /// 开发用：把渲染线程画出来的这一帧从显存读回来存成 PNG。
    /// 覆盖层是 DComp 合成的，抓屏（BitBlt/PrintWindow）拿不到，只能这样确认。
    /// </summary>
    public void CaptureFrame(string path)
    {
        lock (gate)
            captureRequest = path;
    }

    public void Dispose()
    {
        stopRendering();
        if (window != IntPtr.Zero)
        {
            DestroyWindow(window);
            window = IntPtr.Zero;
        }
    }

    // ---- 窗口 ----

    private static void registerWindowClass()
    {
        WndClassEx wc = new()
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            style = 0x0002 | 0x0001, // CS_HREDRAW | CS_VREDRAW
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(windowProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = WindowClass
        };
        RegisterClassEx(ref wc);
    }

    private static readonly WindowProcDelegate windowProc = windowProcedure;

    private static IntPtr windowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        // 点在预览上也要穿透到下面的窗口（它只是块装饰）。
        if (message == 0x0084) // WM_NCHITTEST
            return (IntPtr)(-1); // HTTRANSPARENT
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    // ---- 渲染线程 ----

    private void startRendering()
    {
        if (renderThread is not null)
            return;

        stopSource = new CancellationTokenSource();
        CancellationToken token = stopSource.Token;
        renderThread = new Thread(() =>
        {
            if (useLegacy)
                renderLoop(token);
            else
                renderRetainedLoop(token);
        })
        {
            IsBackground = true,
            Name = "PreviewOverlayRender"
        };
        renderThread.Start();
    }

    private void stopRendering()
    {
        stopSource?.Cancel();
        resizeSignal.Set();
        // Surface/设备由工作线程释放完毕后才允许下一次 Show 创建新线程。
        // 不在超时后丢弃仍然运行的线程，否则两条线程会同时向同一个 HWND 提交。
        renderThread?.Join();
        renderThread = null;
        stopSource?.Dispose();
        stopSource = null;
    }

    private sealed class FrameResources : IDisposable
    {
        public required IDXGISurface Surface { get; init; }
        public required ID2D1RenderTarget Target { get; init; }
        public required ID2D1SolidColorBrush Tap { get; init; }
        public required ID2D1SolidColorBrush Hold { get; init; }
        public required ID2D1SolidColorBrush HoldHead { get; init; }
        public required ID2D1SolidColorBrush Judgement { get; init; }

        public void Dispose()
        {
            Tap.Dispose();
            Hold.Dispose();
            HoldHead.Dispose();
            Judgement.Dispose();
            Target.Dispose();
            Surface.Dispose();
        }
    }

    private void renderLoop(CancellationToken token)
    {
        AppLog.Info($"预览覆盖层：OpaqueMode={OpaqueMode} useOpaque={useOpaque} hwndAlpha={useHwndAlpha}");
        RendererDescription = useOpaque ? (useHwndAlpha ? "transparent HWND flip swap chain" : "opaque flip swap chain") : "legacy swap chain (application cadence)";
        UsesRetainedRenderer = false;
        ID3D11Device? device = null;
        ID3D11DeviceContext? context = null;
        IDXGISwapChain1? swapChain = null;
        IDXGISwapChain3? swapChain3 = null;
        IDCompositionDevice? composition = null;
        IDCompositionDesktopDevice? compositionDesktop = null;
        IDCompositionTarget? compositionTarget = null;
        IDCompositionVisual? visual = null;
        ID2D1Factory1? d2dFactory = null;
        IntPtr frameLatency = IntPtr.Zero;
        int width = 0, height = 0;
        FrameResources?[] resources = new FrameResources?[2];

        try
        {
            // 混合显卡笔记本：默认适配器可能和 DWM/屏幕输出不在同一块 GPU 上，
            // 跨适配器传输是「抖动」的一种可能来源，所以这里把实际用到的适配器记下来，
            // 并支持 --gpu=<序号> 指定。
            IDXGIAdapter? chosenAdapter = pickAdapter();
            // 指定了适配器时必须用 DriverType.Unknown（Hardware + 适配器 是非法组合，会返回 null 设备）。
            D3D11.D3D11CreateDevice(chosenAdapter, chosenAdapter is null ? DriverType.Hardware : DriverType.Unknown,
                DeviceCreationFlags.BgraSupport,
                [Vortice.Direct3D.FeatureLevel.Level_11_0, Vortice.Direct3D.FeatureLevel.Level_10_1],
                out device, out context);
            chosenAdapter?.Dispose();

            (width, height) = (pixelWidth, pixelHeight);

            using (IDXGIDevice ownDevice = device.QueryInterface<IDXGIDevice>())
            using (IDXGIAdapter ownAdapter = ownDevice.GetAdapter())
                AppLog.Info($"预览覆盖层：渲染适配器 = {ownAdapter.Description.Description.Trim()}");

            using (IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>())
            using (IDXGIFactory2 factory = dxgiDevice.GetAdapter().GetParent<IDXGIFactory2>())
            {
                // 合成交换链 + 预乘 alpha：DWM 直接按 alpha 把它们贴到桌面上。
                SwapChainDescription1 description = new()
                {
                    Width = (uint)Math.Max(1, width),
                    Height = (uint)Math.Max(1, PrototypeMode ? height * 2 : height),
                    Format = Format.B8G8R8A8_UNorm,
                    BufferCount = 2,
                    BufferUsage = Usage.RenderTargetOutput,
                    SampleDescription = new SampleDescription(1, 0),
                    SwapEffect = useHwndAlpha ? SwapEffect.Discard : useOpaque ? SwapEffect.FlipDiscard : SwapEffect.FlipSequential,
                    AlphaMode = useOpaque ? Vortice.DXGI.AlphaMode.Ignore : Vortice.DXGI.AlphaMode.Premultiplied,
                    Flags = useHwndAlpha ? SwapChainFlags.None : SwapChainFlags.FrameLatencyWaitableObject
                };
                swapChain = useOpaque ? factory.CreateSwapChainForHwnd(device, window, description)
                    : factory.CreateSwapChainForComposition(device, description);

                if (!useOpaque)
                {
                composition = DComp.DCompositionCreateDevice3<IDCompositionDevice>(dxgiDevice);
                AppLog.Info("预览覆盖层：DComp 设备已创建");
                compositionDesktop = composition.QueryInterface<IDCompositionDesktopDevice>();
                AppLog.Info("预览覆盖层：IDCompositionDesktopDevice 就绪");
                compositionDesktop.CreateTargetForHwnd(window, true, out IDCompositionTarget target);
                compositionTarget = target;
                AppLog.Info("预览覆盖层：CreateTargetForHwnd 成功");
                visual = composition.CreateVisual();
                visual.SetContent(swapChain);
                compositionTarget.SetRoot(visual);
                composition.Commit();
                AppLog.Info("预览覆盖层：Commit 成功（视觉树已挂上交换链）");
                }
            }

            swapChain3 = swapChain.QueryInterface<IDXGISwapChain3>();
            frameLatency = IntPtr.Zero;
            try
            {
                using IDXGISwapChain2 latencyChain = swapChain.QueryInterface<IDXGISwapChain2>();
                latencyChain.MaximumFrameLatency = (uint)(PaceMode == 2 ? 2 : 1);
                frameLatency = PaceMode is 1 or 3 ? IntPtr.Zero : latencyChain.FrameLatencyWaitableObject;
            }
            catch (Exception exception)
            {
                AppLog.Error("预览覆盖层：拿不到帧延迟等待对象，退回 Present 自身节拍", exception);
            }
            if (PaceMode == 3)
                timeBeginPeriod(1);   // 让 Sleep(1) 真的睡 1ms，而不是 15.6ms

            d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.SingleThreaded);
            AppLog.Info($"预览覆盖层：D3D11 + DirectComposition 就绪，{width}x{height}");

            if (PrototypeMode && !useOpaque)
            {
                // 原型不依赖锚点：窗口尺寸没量到就先用一个固定大小，保证能看到东西。
                runAnimationPrototype(device, swapChain, visual!, composition!, d2dFactory,
                    Math.Max(300, width), Math.Max(420, height), token);
                return;
            }

            Stopwatch frameClock = Stopwatch.StartNew();
            double lastFrame = 0;

            while (!token.IsCancellationRequested)
            {
                if (resizeSignal.Wait(0))
                {
                    resizeSignal.Reset();
                    if (pixelWidth != width || pixelHeight != height)
                    {
                        foreach (FrameResources? item in resources)
                            item?.Dispose();
                        Array.Clear(resources);

                        width = pixelWidth;
                        height = pixelHeight;
                        if (width > 1 && height > 1)
                        {
                            // 创建时带了 FrameLatencyWaitableObject，ResizeBuffers 必须保留同一个标志，
                            // 否则帧延迟句柄会失效（DXGI 的明确要求）。
                            SharpGen.Runtime.Result resizeResult = swapChain.ResizeBuffers(2, (uint)width, (uint)height,
                                Format.B8G8R8A8_UNorm, useHwndAlpha ? SwapChainFlags.None : SwapChainFlags.FrameLatencyWaitableObject);
                            if (resizeResult.Failure)
                                AppLog.Info($"预览覆盖层：ResizeBuffers 失败 {resizeResult.Description}");
                        }
                    }
                }

                if (width <= 1 || height <= 1)
                {
                    Thread.Sleep(15);
                    continue;
                }

                double waitStart = frameClock.Elapsed.TotalMilliseconds;
                if (frameLatency != IntPtr.Zero && WaitForSingleObject(frameLatency, 100) == WaitTimeout)
                {
                    Thread.Sleep(4);
                    continue;
                }
                double waitMs = frameClock.Elapsed.TotalMilliseconds - waitStart;

                bool transparentPending;
                lock (gate)
                {
                    transparentPending = transparentFramePending;
                    transparentFramePending = false;
                }

                ManiaPreviewChart? currentChart;
                lock (gate)
                    currentChart = chart;

                if (transparentPending || (currentChart is null && hasContentOnScreen))
                {
                    // 清屏：画一帧全透明并提交，然后停在这里，不再提交。
                    int clearIndex = (int)swapChain3.CurrentBackBufferIndex;
                    resources[clearIndex] ??= createFrameResources(d2dFactory, swapChain, clearIndex);
                    ID2D1RenderTarget clearTarget = resources[clearIndex]!.Target;
                    clearTarget.BeginDraw();
                    clearTarget.Clear(new Color4(0, 0, 0, 0));
                    clearTarget.EndDraw(out _, out _);
                    swapChain.Present(0, PresentFlags.None);
                    hasContentOnScreen = false;
                    Thread.Sleep(20);
                    continue;
                }

                if (currentChart is null)
                {
                    // 没内容也没东西可清：完全空闲，别空转。
                    Thread.Sleep(20);
                    continue;
                }

                int index = (int)swapChain3.CurrentBackBufferIndex;
                resources[index] ??= createFrameResources(d2dFactory, swapChain, index);

                double nowMs = frameClock.Elapsed.TotalMilliseconds;
                ManiaPreviewChart? snapshot;
                double timeMs;
                double visibleSeconds;
                double latency;
                double opacity;
                lock (gate)
                {
                    clock.Advance(now.Elapsed.TotalMilliseconds, Math.Clamp(nowMs - lastFrame, 0, 120));
                    // 延迟补偿：直接往未来多看一点，抵消 tosu 采样 + 合成的那一两帧。
                    timeMs = clock.Time + LatencyMilliseconds;
                    visibleSeconds = VisibleSeconds;
                    latency = LatencyMilliseconds;
                    opacity = Opacity;
                    snapshot = chart;
                }

                double drawStart = frameClock.Elapsed.TotalMilliseconds;
                draw(resources[index]!, width, height, snapshot, timeMs, visibleSeconds, opacity);
                hasContentOnScreen = true;
                double drawMs = frameClock.Elapsed.TotalMilliseconds - drawStart;

                string? pendingCapture;
                lock (gate)
                {
                    pendingCapture = captureRequest;
                    captureRequest = null;
                }
                if (pendingCapture is not null)
                    saveCurrentFrame(device, swapChain, index, pendingCapture);

                LastDrawMilliseconds = drawMs;
                double presentStart = frameClock.Elapsed.TotalMilliseconds;
                // 同步间隔先用 1：SyncInterval=2 在 hwnd 翻转交换链上可能把「上一块空缓冲」留在屏上，
                // 现场表现就是「黑底在、音符画了但看不见」。限帧改成软件限（下面按时间跳过）。
                swapChain.Present(PaceMode == 3 ? 0u : 1u, PresentFlags.None);
                double presentMs = frameClock.Elapsed.TotalMilliseconds - presentStart;

                if (PaceMode == 3)
                    Thread.Sleep(1);   // 提交得比屏幕快一点：每个 vblank 都能采到新帧

                if (frameClock.Elapsed.TotalSeconds - lastStatusLog > 2)
                {
                    lastStatusLog = frameClock.Elapsed.TotalSeconds;
                    AppLog.Info($"预览状态：time={timeMs:0}ms 画了={DrawnNotes} 物件={snapshot?.Notes.Count ?? 0} " +
                                $"尺寸={width}x{height} 可见={VisibleSeconds:0.00}s");
                }

                double interval = nowMs - lastFrame;
                lastFrame = nowMs;

                lock (gate)
                {
                    // 预热帧（首帧建资源、读回截图、刚 Resize 过）不进样本，否则 p99 会被它们污染。
                    if (warmupFrames > 0)
                    {
                        warmupFrames--;
                    }
                    else if (interval > 0)
                    {
                        frames.Add(interval);
                        if (!double.IsNaN(lastContentTime))
                        {
                            double contentDelta = timeMs - lastContentTime;
                            if (contentDelta <= 0)
                                contentBackwards++;
                            else
                                contentSamples.Add(contentDelta);
                        }
                        lastContentTime = timeMs;
                        waitSamples.Add(waitMs);
                        drawSamples.Add(drawMs);
                        presentSamples.Add(presentMs);
                        if (frames.Count > 1800)
                        {
                            frames.RemoveRange(0, frames.Count - 1800);
                            waitSamples.RemoveRange(0, waitSamples.Count - 1800);
                            drawSamples.RemoveRange(0, drawSamples.Count - 1800);
                            presentSamples.RemoveRange(0, presentSamples.Count - 1800);
                        }
                    }
                }

                _ = latency;
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("预览覆盖层渲染线程异常退出", exception);
        }
        finally
        {
            if (PaceMode == 3) timeEndPeriod(1);
            foreach (FrameResources? resource in resources)
                resource?.Dispose();
            d2dFactory?.Dispose();
            visual?.Dispose();
            compositionTarget?.Dispose();
            compositionDesktop?.Dispose();
            composition?.Dispose();
            swapChain3?.Dispose();
            swapChain?.Dispose();
            context?.Dispose();
            device?.Dispose();
        }
    }

    /// <summary>按 --gpu 选择适配器；-1 表示用系统默认的那块。顺便把所有适配器记进日志。</summary>
    private static IDXGIAdapter? pickAdapter()
    {
        try
        {
            using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            List<(int Index, string Name, IDXGIAdapter Adapter)> all = [];
            for (uint index = 0; ; index++)
            {
                if (factory.EnumAdapters1(index, out IDXGIAdapter1? adapter).Failure || adapter is null)
                    break;
                all.Add(((int)index, adapter.Description.Description.Trim(), adapter));
            }

            if (all.Count > 0)
                AppLog.Info($"预览覆盖层：可用适配器 {string.Join("｜", all.Select(a => $"[{a.Index}] {a.Name}"))}");

            if (AdapterIndex >= 0)
            {
                (int Index, string Name, IDXGIAdapter Adapter) pick = all.FirstOrDefault(a => a.Index == AdapterIndex);
                if (pick.Adapter is not null)
                {
                    AppLog.Info($"预览覆盖层：按 --gpu={AdapterIndex} 使用 {pick.Name}");
                    foreach (var entry in all)
                        if (entry.Adapter != pick.Adapter)
                            entry.Adapter.Dispose();
                    return pick.Adapter;
                }

                AppLog.Info($"预览覆盖层：--gpu={AdapterIndex} 不存在，回退默认适配器");
            }

            foreach ((int Index, string Name, IDXGIAdapter Adapter) entry in all)
                entry.Adapter.Dispose();
            return null;
        }
        catch (Exception exception)
        {
            AppLog.Error("预览覆盖层：枚举适配器失败", exception);
            return null;
        }
    }

    /// <summary>
    /// 最小验证：一块 2 屏高的表面（上下两半图案相同），用 DComp 动画把它从 -H 匀速平移到 0，
    /// 配合 AddRepeat 无限循环 —— 之后**应用线程不再提交任何一帧**，画面完全由合成器推进。
    /// 如果这样看着是顺的，就说明「合成器动画」这条路可行，再把谱面分块接进来。
    /// </summary>
    private void runAnimationPrototype(ID3D11Device device, IDXGISwapChain1 swapChain,
        IDCompositionVisual visual, IDCompositionDevice composition,
        ID2D1Factory1 factory, int width, int height, CancellationToken token)
    {
        // 用**此刻**的窗口尺寸（线程起来时可能是 1x1，之后又被锚点改过）。
        width = Math.Max(1, pixelWidth);
        height = Math.Max(1, pixelHeight);
        int tallHeight = Math.Max(2, height * 2);
        // 渲染线程可能在窗口还是 1x1 的时候就起来了（交换链也是 1x1），
        // 所以这里按目标尺寸重建缓冲，并把内容重新挂到视觉上。
        SharpGen.Runtime.Result resize = swapChain.ResizeBuffers(2, (uint)Math.Max(1, width),
            (uint)Math.Max(1, tallHeight), Format.B8G8R8A8_UNorm, SwapChainFlags.FrameLatencyWaitableObject);
        AppLog.Info($"预览覆盖层：原型 重建缓冲到 {width}x{tallHeight}（{(resize.Failure ? resize.Description : "OK")}）");
        visual.SetContent(swapChain);
        composition.Commit();

        using (IDXGISurface surface = swapChain.GetBuffer<IDXGISurface>(0))
        {
            RenderTargetProperties properties = new(
                RenderTargetType.Default,
                new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96, 96, RenderTargetUsage.None, Vortice.Direct2D1.FeatureLevel.Default);
            using ID2D1RenderTarget target = factory.CreateDxgiSurfaceRenderTarget(surface, properties);
            using ID2D1SolidColorBrush brush = target.CreateSolidColorBrush(new Color4(0xF5 / 255f, 0xF9 / 255f, 0xFB / 255f, 1f));

            target.BeginDraw();
            target.Clear(new Color4(0, 0, 0, 0));
            // 上下两半画同样的条纹：平移一个 H 之后内容完全重合，所以循环是无缝的。
            double barHeight = Math.Max(3, height / 60.0);
            for (double y = 0; y < height; y += height / 12.0)
            {
                target.FillRectangle(rect(0, y, width, barHeight), brush);
                target.FillRectangle(rect(0, y + height, width, barHeight), brush);
            }
            target.EndDraw(out _, out _);
        }

        SharpGen.Runtime.Result firstPresent = swapChain.Present(0, PresentFlags.None);
        using (IDXGISurface probe = swapChain.GetBuffer<IDXGISurface>(0))
            AppLog.Info($"预览覆盖层：原型模式，窗口 {pixelWidth}x{pixelHeight}，表面 {width}x{tallHeight}，" +
                        $"实际缓冲 {(int)probe.Description.Width}x{(int)probe.Description.Height}，" +
                        $"首次 Present={(firstPresent.Failure ? firstPresent.Description : "OK")}");

        // 第一步：不放动画，offset = 0 静态显示。先确认「内容能不能出来」，
        // 再把「动画参数对不对」分开验证。
        visual.SetOffsetY(0f);
        composition.Commit();
        AppLog.Info("预览覆盖层：原型 静态 commit 完成（offset=0）");

        // 静态阶段：每 4ms 再提交一次（排除「只 Present 一次所以没内容」这个可能）。
        int presented = 0;
        SharpGen.Runtime.Result lastResult = default;
        for (int i = 0; i < 300 && !token.IsCancellationRequested; i++)
        {
            lastResult = swapChain.Present(0, PresentFlags.None);
            presented++;
            Thread.Sleep(4);
        }

        AppLog.Info($"预览覆盖层：原型 静态阶段提交 {presented} 次，" +
                    $"最后一次={(lastResult.Failure ? lastResult.Description : "OK")}");

        // AddCubic 的参数是常数、一次、二次、三次系数；一次项即可得到匀速直线。
        float duration = (float)Math.Max(0.25, VisibleSeconds);
        float slope = (float)height / duration;
        using IDCompositionAnimation animation = composition.CreateAnimation();
        animation.AddCubic(0, -height, slope, 0, 0);
        animation.AddRepeat(duration, duration);
        animation.End(86400, 0f);
        visual.SetOffsetY(animation);
        composition.Commit();
        AppLog.Info($"预览覆盖层：原型 动画已挂上（{duration:0.00}s/屏，线性循环）");

        while (!token.IsCancellationRequested)
            Thread.Sleep(50);
    }

    private static FrameResources createFrameResources(ID2D1Factory1 factory, IDXGISwapChain1 swapChain, int index)
    {
        IDXGISurface surface = swapChain.GetBuffer<IDXGISurface>((uint)index);
        RenderTargetProperties properties = new(
            RenderTargetType.Default,
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            96, 96,
            RenderTargetUsage.None,
            Vortice.Direct2D1.FeatureLevel.Default);

        ID2D1RenderTarget target = factory.CreateDxgiSurfaceRenderTarget(surface, properties);
        return new FrameResources
        {
            Surface = surface,
            Target = target,
            Tap = target.CreateSolidColorBrush(new Color4(0xF5 / 255f, 0xF9 / 255f, 0xFB / 255f, 1f)),
            Hold = target.CreateSolidColorBrush(new Color4(0xE1 / 255f, 0x51 / 255f, 0x7B / 255f, 1f)),
            HoldHead = target.CreateSolidColorBrush(new Color4(0xFB / 255f, 0xF2 / 255f, 0xF5 / 255f, 1f)),
            Judgement = target.CreateSolidColorBrush(new Color4(0x5A / 255f, 0x6A / 255f, 0x76 / 255f, 1f))
        };
    }

    private static D2DRect rect(double x, double y, double width, double height) =>
        new((float)x, (float)y, (float)Math.Max(0, width), (float)Math.Max(0, height));

    /// <summary>只画音符：轨道、判定线、键带、标题全部不画，其余像素保持透明。</summary>
    private void draw(FrameResources resources, int width, int height, ManiaPreviewChart? chart,
        double timeMs, double visibleSeconds, double opacity)
    {
        ID2D1RenderTarget target = resources.Target;
        target.BeginDraw();
        if (OpaqueSelfTest)
        {
            // 诊断用：整块画成品红。若面板是品红 → 画+显示都通，问题在音符绘制；
            // 若仍是黑的 → 这条 hwnd 交换链的显示链路本身没生效。
            target.Clear(new Color4(1f, 0f, 1f, 1f));
            target.EndDraw(out _, out _);
            return;
        }

        target.Clear(useOpaque && !useHwndAlpha ? new Color4(0.055f, 0.071f, 0.094f, 1) : new Color4(0, 0, 0, 0));
        DrawnNotes = 0;

        if (chart is null || width <= 1 || height <= 1)
        {
            target.EndDraw();
            return;
        }

        int keys = chart.KeyCount;
        double laneWidth = (double)width / keys;
        double pixelsPerMs = height / Math.Max(0.25, visibleSeconds) / 1000.0;
        double noteHeight = Math.Clamp(laneWidth * 0.17, 6, 18);
        double noteWidth = Math.Max(2, laneWidth - 6);
        // 顶部淡入：按**屏幕**位置算透明度（不是按谱面时间），所以音符进入画面时是渐显的。
        // FadeEnabled=false 时传 0，fillFaded 会整块按不透明画。
        double fadeHeight = FadeEnabled ? Math.Max(24, height * 0.18) : 0;

        IReadOnlyList<PreviewNote> notes = chart.Notes;
        double visibleMs = visibleSeconds * 1000;
        int drawn = 0;

        for (int index = chart.FirstIndexAt(timeMs - chart.LongestHold - 1); index < notes.Count; index++)
        {
            PreviewNote note = notes[index];
            if (note.Start - timeMs > visibleMs)
                break;
            if (note.End < timeMs)
                continue;

            double headY = height - (note.Start - timeMs) * pixelsPerMs;
            double tailY = height - (note.End - timeMs) * pixelsPerMs;
            double x = note.Column * laneWidth + 3;

            if (note.IsHold && headY - tailY > 0.5)
            {
                // 长条按像素分段淡入：顶端几乎透明，进入画面后才完全不透明。
                // 整条共用一个透明度的话，长条要「完全出现」才会变实 —— 就是之前那个问题。
                double bodyTop = Math.Max(tailY, 0);
                double bodyBottom = Math.Min(headY, height);
                if (bodyBottom - bodyTop > 0.5)
                    fillFaded(target, resources.Hold, x, bodyTop, noteWidth, bodyBottom - bodyTop, fadeHeight);

                if (headY >= 0 && headY < height)
                    fillFaded(target, resources.HoldHead, x, headY, noteWidth, Math.Min(noteHeight, height - headY), fadeHeight);
            }
            else
            {
                double y = Math.Max(headY, 0);
                if (y >= height)
                    continue;
                fillFaded(target, resources.Tap, x, y, noteWidth, Math.Min(noteHeight, height - y), fadeHeight);
            }

            drawn++;
        }

        DrawnNotes = drawn;

        // 底部判定线：位置固定，不随音符滚动。
        resources.Judgement.Opacity = 1;
        target.FillRectangle(rect(0, height - 2.5, width, 1.5), resources.Judgement);

        if (opacity < 0.999)
            target.EndDraw();
        else
            target.EndDraw(out _, out _);
    }

    /// <summary>
    /// 画一块矩形，但按**屏幕位置**给它逐段透明度：顶部淡入区的上半部分几乎透明，
    /// 越往下越实。长条因此是「从顶端慢慢显出来」，而不是整条一起跳。
    /// </summary>
    private static void fillFaded(ID2D1RenderTarget target, ID2D1SolidColorBrush brush,
        double x, double top, double width, double height, double fadeHeight)
    {
        double bottom = top + height;
        if (height <= 0 || bottom <= 0)
            return;

        top = Math.Max(top, 0);
        if (top >= fadeHeight)
        {
            brush.Opacity = 1;
            target.FillRectangle(rect(x, top, width, bottom - top), brush);
            return;
        }

        // 淡入区里按 6px 切片（切太粗会有色带，切太细没必要）。
        const double slice = 6;
        double cursor = top;
        double fadeEnd = Math.Min(bottom, fadeHeight);
        while (cursor < fadeEnd)
        {
            double next = Math.Min(cursor + slice, fadeEnd);
            brush.Opacity = (float)Math.Clamp(((cursor + next) * 0.5) / fadeHeight, 0.04, 1);
            target.FillRectangle(rect(x, cursor, width, next - cursor), brush);
            cursor = next;
        }

        if (bottom > fadeHeight)
        {
            brush.Opacity = 1;
            target.FillRectangle(rect(x, fadeHeight, width, bottom - fadeHeight), brush);
        }
    }

    private static void saveCurrentFrame(ID3D11Device device, IDXGISwapChain1 swapChain, int index, string path)
    {
        try
        {
            using ID3D11Texture2D backBuffer = swapChain.GetBuffer<ID3D11Texture2D>((uint)index);
            Texture2DDescription description = backBuffer.Description;
            Texture2DDescription stagingDescription = description with
            {
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Read,
                MiscFlags = ResourceOptionFlags.None
            };

            using ID3D11Texture2D staging = device.CreateTexture2D(stagingDescription);
            using ID3D11DeviceContext context = device.ImmediateContext;
            context.CopyResource(staging, backBuffer);

            MappedSubresource map = context.Map(staging, 0, MapMode.Read);
            try
            {
                int width = (int)description.Width;
                int height = (int)description.Height;
                int stride = (int)map.RowPitch;
                byte[] pixels = new byte[width * height * 4];
                for (int row = 0; row < height; row++)
                    Marshal.Copy(map.DataPointer + row * stride, pixels, row * width * 4, width * 4);

                System.Windows.Media.Imaging.BitmapSource bitmap =
                    System.Windows.Media.Imaging.BitmapSource.Create(width, height, 96, 96,
                        System.Windows.Media.PixelFormats.Bgra32, null, pixels, width * 4);

                System.Windows.Media.Imaging.PngBitmapEncoder encoder = new();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using FileStream stream = File.Create(path);
                encoder.Save(stream);
                AppLog.Info($"预览覆盖层：已读回一帧 {width}x{height} -> {path}");
            }
            finally
            {
                context.Unmap(staging, 0);
            }
        }
        catch (Exception exception)
        {
            AppLog.Error($"预览覆盖层：读回当前帧失败 {path}", exception);
        }
    }

    // ---- Win32 ----
    [StructLayout(LayoutKind.Sequential)]
    private struct BlurBehind
    {
        public uint Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Enable;
        public IntPtr Region;
        [MarshalAs(UnmanagedType.Bool)] public bool TransitionOnMaximized;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref BlurBehind blur);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);

    private delegate IntPtr WindowProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out WindowRect rect);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    private const uint WaitTimeout = 258;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    private const int GwlpHwndParent = -8;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WndClassEx wndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(WindowExStyles exStyle, string className, string windowName,
        WindowStyles style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, SetWindowPosFlags flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, ShowWindowCommand command);

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint milliseconds);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    private const int GwlStyle = -16;
    private const long WsChild = 0x40000000L;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out WindowRect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [Flags]
    private enum WindowStyles : uint
    {
        WS_POPUP = 0x80000000
    }

    [Flags]
    private enum WindowExStyles : uint
    {
        WS_EX_NOREDIRECTIONBITMAP = 0x00200000,
        WS_EX_TRANSPARENT = 0x00000020,
        WS_EX_NOACTIVATE = 0x08000000,
        WS_EX_TOOLWINDOW = 0x00000080,
        WS_EX_TOPMOST = 0x00000008
    }

    private enum ShowWindowCommand
    {
        SW_HIDE = 0,
        SW_SHOWNOACTIVATE = 4
    }

    [Flags]
    private enum SetWindowPosFlags : uint
    {
        SWP_NOZORDER = 0x0004,
        SWP_NOSIZE = 0x0001,
        SWP_NOMOVE = 0x0002,
        SWP_NOACTIVATE = 0x0010
    }
}
