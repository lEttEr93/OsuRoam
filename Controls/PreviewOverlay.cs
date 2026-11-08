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
public sealed class PreviewOverlay : IDisposable
{
    private const string WindowClass = "OsuRoamPreviewOverlay";

    private readonly object gate = new();
    private readonly Stopwatch now = Stopwatch.StartNew();
    private readonly List<double> frames = [];
    private readonly List<double> waitSamples = [];
    private readonly List<double> drawSamples = [];
    private readonly List<double> presentSamples = [];
    private readonly ManualResetEventSlim resizeSignal = new(false);

    private ManiaPreviewChart? chart;
    private PreviewClock clock = new();
    private string? captureRequest;
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
            WindowExStyles.WS_EX_NOREDIRECTIONBITMAP | WindowExStyles.WS_EX_TRANSPARENT |
            WindowExStyles.WS_EX_NOACTIVATE | WindowExStyles.WS_EX_TOOLWINDOW | WindowExStyles.WS_EX_TOPMOST,
            WindowClass, "osu! Roam preview", WindowStyles.WS_POPUP,
            pixelLeft, pixelTop, pixelWidth, pixelHeight,
            IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

        if (window == IntPtr.Zero)
            AppLog.Error($"预览覆盖层：创建窗口失败（错误码 {Marshal.GetLastWin32Error()}）");
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

    /// <summary>创建 D3D 设备用的适配器序号（-1 = 默认硬件适配器）。</summary>
    public static int AdapterIndex { get; set; } = -1;
    public int DrawnNotes { get; private set; }

    /// <summary>把预览挂到信息窗下面：owner 关闭时它跟着消失，也不会跑到 owner 后面去。</summary>
    public void SetOwner(IntPtr hwnd)
    {
        if (hwnd == owner || window == IntPtr.Zero)
            return;
        owner = hwnd;
        SetWindowLongPtr(window, GwlpHwndParent, hwnd);
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

        ShowWindow(window, ShowWindowCommand.SW_SHOWNOACTIVATE);
        startRendering();
    }

    public void Hide()
    {
        lock (gate)
            showing = false;
        hide();
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
        lock (gate)
            clock.Sync(milliseconds, now.Elapsed.TotalMilliseconds);
    }

    public void ResetFrameStats()
    {
        lock (gate)
        {
            frames.Clear();
            waitSamples.Clear();
            drawSamples.Clear();
            presentSamples.Clear();
            warmupFrames = WarmupFrames;
        }
    }

    /// <summary>清空后先跳过这么多帧再采样（首帧要建资源，混进去会让 p99 失真）。</summary>
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
        renderThread = new Thread(() => renderLoop(token))
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
        try { renderThread?.Join(1500); } catch { }
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

            using (IDXGIDevice ownDevice = device.QueryInterface<IDXGIDevice>())
            using (IDXGIAdapter ownAdapter = ownDevice.GetAdapter())
                AppLog.Info($"预览覆盖层：渲染适配器 = {ownAdapter.Description.Description.Trim()}");

            using (IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>())
            using (IDXGIFactory2 factory = dxgiDevice.GetAdapter().GetParent<IDXGIFactory2>())
            {
                // 合成交换链 + 预乘 alpha：DWM 直接按 alpha 把它们贴到桌面上。
                swapChain = factory.CreateSwapChainForComposition(device, new SwapChainDescription1
                {
                    Width = (uint)Math.Max(1, width),
                    Height = (uint)Math.Max(1, height),
                    Format = Format.B8G8R8A8_UNorm,
                    BufferCount = 2,
                    BufferUsage = Usage.RenderTargetOutput,
                    SampleDescription = new SampleDescription(1, 0),
                    SwapEffect = SwapEffect.FlipSequential,
                    AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied,
                    Flags = SwapChainFlags.FrameLatencyWaitableObject
                });

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

            swapChain3 = swapChain.QueryInterface<IDXGISwapChain3>();
            frameLatency = IntPtr.Zero;
            try
            {
                using IDXGISwapChain2 latencyChain = swapChain.QueryInterface<IDXGISwapChain2>();
                latencyChain.MaximumFrameLatency = (uint)(PaceMode == 2 ? 2 : 1);
                frameLatency = PaceMode == 1 ? IntPtr.Zero : latencyChain.FrameLatencyWaitableObject;
            }
            catch (Exception exception)
            {
                AppLog.Error("预览覆盖层：拿不到帧延迟等待对象，退回 Present 自身节拍", exception);
            }
            d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.SingleThreaded);
            AppLog.Info($"预览覆盖层：D3D11 + DirectComposition 就绪，{width}x{height}");

            FrameResources?[] resources = new FrameResources?[2];
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
                                Format.B8G8R8A8_UNorm, SwapChainFlags.FrameLatencyWaitableObject);
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
                    clock.Advance(nowMs, Math.Clamp(nowMs - lastFrame, 0, 120));
                    // 延迟补偿：直接往未来多看一点，抵消 tosu 采样 + 合成的那一两帧。
                    timeMs = clock.Time + LatencyMilliseconds;
                    visibleSeconds = VisibleSeconds;
                    latency = LatencyMilliseconds;
                    opacity = Opacity;
                    snapshot = chart;
                }

                double drawStart = frameClock.Elapsed.TotalMilliseconds;
                draw(resources[index]!, width, height, snapshot, timeMs, visibleSeconds, opacity);
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
                swapChain.Present(1, PresentFlags.None);
                double presentMs = frameClock.Elapsed.TotalMilliseconds - presentStart;

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
        target.Clear(new Color4(0, 0, 0, 0));
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
        double fadeHeight = Math.Max(24, height * 0.18);

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

    private delegate IntPtr WindowProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

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

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

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
        SWP_NOACTIVATE = 0x0010
    }
}
