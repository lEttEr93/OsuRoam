using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using OsuRoam.Services;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Color4 = Vortice.Mathematics.Color4;
using D2DRect = Vortice.Mathematics.Rect;
using HandleRef = System.Runtime.InteropServices.HandleRef;

namespace OsuRoam.Controls;

/// <summary>
/// 谱面预览的独立渲染管线：一个子 HWND + 自己的 D3D11 翻转交换链 + 自己的线程。
///
/// 为什么不用 WPF 画：WPF 是保留模式、由 UI 线程驱动，主线程上任何活（tosu 快照、
/// 封面解码、键型计算、GC）都会把帧挤掉 —— 测出来控件自己的绘制只要 0.04ms，p95 却
/// 仍有 16–20ms。这条链路整段和 WPF 无关：设备、交换链、绘制、时钟外推都在渲染线程里，
/// 再用帧延迟等待对象把节奏锁在显示器的合成节拍上。
///
/// 翻转模型（FLIP_*）的交换链**每帧缓冲区会轮转**，所以渲染目标要按缓冲区索引各自
/// 缓存一份（画笔绑定在渲染目标上，也要各一套），不能在 Present 之后继续画同一块。
///
/// 和外面的契约只有两个：<see cref="SetChart"/> 换谱面、<see cref="Sync"/> 喂 tosu 报的
/// 预览音频位置；两者都只是写字段，UI 线程永远不会阻塞在渲染上。
/// </summary>
public sealed class PreviewSurface : HwndHost
{
    private readonly object gate = new();
    private readonly Stopwatch now = Stopwatch.StartNew();
    private readonly List<double> frames = [];
    private readonly ManualResetEventSlim resized = new(false);

    private ManiaPreviewChart? chart;
    private string? captureRequest;
    private PreviewClock clock = new();

    private Thread? renderThread;
    private CancellationTokenSource? stopSource;
    private IntPtr child;
    private int pixelWidth;
    private int pixelHeight;

    /// <summary>判定线离底边留多高：下面这条带子画键位，不做判定。</summary>
    private const double KeyBandHeight = 26;

    public double VisibleSeconds { get; set; } = 1.25;

    /// <summary>最近几秒的帧间隔（毫秒），给帧率探针用。</summary>
    public IReadOnlyList<double> FrameIntervals
    {
        get
        {
            lock (gate)
                return [.. frames];
        }
    }

    /// <summary>最近一帧画了多少个物件（开发用）。</summary>
    public int DrawnNotes { get; private set; }

    /// <summary>最近一帧的绘制耗时（毫秒，不含等待节拍）。</summary>
    public double LastDrawMilliseconds { get; private set; }

    public void SetChart(ManiaPreviewChart? value)
    {
        lock (gate)
        {
            chart = value;
            clock = new PreviewClock();
        }
    }

    public void Sync(double milliseconds)
    {
        lock (gate)
            clock.Sync(milliseconds, now.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// 开发用：把渲染线程画出来的这一帧从显存读回来存成 PNG。
    /// 用抓屏（BitBlt/PrintWindow）看翻转交换链经常是黑的，只能这样确认真的画对了。
    /// </summary>
    public void CaptureFrame(string path)
    {
        lock (gate)
            captureRequest = path;
    }

    public void ResetFrameStats()
    {
        lock (gate)
            frames.Clear();
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        (pixelWidth, pixelHeight) = physicalSize();
        child = CreateWindowEx(0, "static", string.Empty,
            WindowStyles.WS_CHILD | WindowStyles.WS_VISIBLE | WindowStyles.WS_CLIPSIBLINGS,
            0, 0, Math.Max(1, pixelWidth), Math.Max(1, pixelHeight),
            hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        stopSource = new CancellationTokenSource();
        renderThread = new Thread(() => renderLoop(child, stopSource.Token))
        {
            IsBackground = true,
            Name = "PreviewRender"
        };
        renderThread.Start();

        return new HandleRef(this, child);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        stopSource?.Cancel();
        resized.Set();
        try { renderThread?.Join(1500); } catch { }
        renderThread = null;
        stopSource?.Dispose();
        stopSource = null;

        if (hwnd.Handle != IntPtr.Zero)
            DestroyWindow(hwnd.Handle);
        child = IntPtr.Zero;
    }

    protected override void OnWindowPositionChanged(System.Windows.Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);

        (int width, int height) = physicalSize();
        if (width == pixelWidth && height == pixelHeight)
            return;

        pixelWidth = width;
        pixelHeight = height;
        if (child != IntPtr.Zero)
            SetWindowPos(child, IntPtr.Zero, 0, 0, Math.Max(1, width), Math.Max(1, height),
                SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE);
        resized.Set();
    }

    /// <summary>WPF 的尺寸是 DIP，交换链要物理像素。</summary>
    private (int Width, int Height) physicalSize()
    {
        double scale = 1;
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
            scale = target.TransformToDevice.M11;
        return (Math.Max(1, (int)Math.Round(ActualWidth * scale)),
                Math.Max(1, (int)Math.Round(ActualHeight * scale)));
    }

    // ---- 渲染线程 ----

    /// <summary>一个交换链缓冲区对应的一整套 D2D 资源。</summary>
    private sealed class FrameResources : IDisposable
    {
        public required IDXGISurface Surface { get; init; }
        public required ID2D1RenderTarget Target { get; init; }
        public required ID2D1SolidColorBrush Lane { get; init; }
        public required ID2D1SolidColorBrush LaneAlt { get; init; }
        public required ID2D1SolidColorBrush Tap { get; init; }
        public required ID2D1SolidColorBrush Hold { get; init; }
        public required ID2D1SolidColorBrush HoldHead { get; init; }
        public required ID2D1SolidColorBrush Judgement { get; init; }
        public required ID2D1SolidColorBrush KeyBand { get; init; }

        public void Dispose()
        {
            Lane.Dispose();
            LaneAlt.Dispose();
            Tap.Dispose();
            Hold.Dispose();
            HoldHead.Dispose();
            Judgement.Dispose();
            KeyBand.Dispose();
            Target.Dispose();
            Surface.Dispose();
        }
    }

    private void renderLoop(IntPtr hwnd, CancellationToken token)
    {
        ID3D11Device? device = null;
        ID3D11DeviceContext? context = null;
        IDXGISwapChain1? swapChain = null;
        IDXGISwapChain2? swapChain2 = null;
        IDXGISwapChain3? swapChain3 = null;
        ID2D1Factory1? d2dFactory = null;
        IntPtr frameLatency = IntPtr.Zero;
        int width = 0, height = 0;
        bool presentErrorLogged = false;

        try
        {
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                [Vortice.Direct3D.FeatureLevel.Level_11_0, Vortice.Direct3D.FeatureLevel.Level_10_1],
                out device, out context);

            using (IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>())
            using (IDXGIAdapter adapter = dxgiDevice.GetAdapter())
            using (IDXGIFactory2 factory = adapter.GetParent<IDXGIFactory2>())
            {
                (width, height) = (pixelWidth, pixelHeight);
                swapChain = factory.CreateSwapChainForHwnd(device, hwnd, new SwapChainDescription1
                {
                    Width = (uint)Math.Max(1, width),
                    Height = (uint)Math.Max(1, height),
                    Format = Format.B8G8R8A8_UNorm,
                    BufferCount = 2,
                    BufferUsage = Usage.RenderTargetOutput,
                    SampleDescription = new SampleDescription(1, 0),
                    SwapEffect = SwapEffect.FlipDiscard,
                    AlphaMode = Vortice.DXGI.AlphaMode.Ignore,
                    // 没有这个标记，SetMaximumFrameLatency / 帧延迟句柄都会被 DXGI 判为非法调用。
                    Flags = SwapChainFlags.FrameLatencyWaitableObject
                });
            }

            swapChain2 = swapChain.QueryInterface<IDXGISwapChain2>();
            swapChain3 = swapChain.QueryInterface<IDXGISwapChain3>();
            swapChain2.MaximumFrameLatency = 1;
            try
            {
                frameLatency = swapChain2.FrameLatencyWaitableObject;
            }
            catch (Exception exception)
            {
                AppLog.Error("拿不到帧延迟等待对象，退化为定时器节拍", exception);
                frameLatency = IntPtr.Zero;
            }

            d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.SingleThreaded);
            AppLog.Info($"预览渲染管线：D3D11 就绪，交换链 {width}x{height}，" +
                        $"节拍={(frameLatency == IntPtr.Zero ? "定时器" : "帧延迟等待对象")}");

            FrameResources?[] resources = new FrameResources?[2];
            Stopwatch frameClock = Stopwatch.StartNew();
            double lastFrame = 0;

            while (!token.IsCancellationRequested)
            {
                if (resized.Wait(0))
                {
                    resized.Reset();
                    if (pixelWidth != width || pixelHeight != height)
                    {
                        foreach (FrameResources? item in resources)
                            item?.Dispose();
                        Array.Clear(resources);

                        width = pixelWidth;
                        height = pixelHeight;
                        if (width > 1 && height > 1)
                        {
                            swapChain.ResizeBuffers(2, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
                            AppLog.Info($"预览渲染管线：交换链尺寸改为 {width}x{height}");
                        }
                    }
                }

                if (width <= 1 || height <= 1)
                {
                    Thread.Sleep(15);
                    continue;
                }

                if (frameLatency != IntPtr.Zero && WaitForSingleObject(frameLatency, 100) == WaitTimeout)
                {
                    // 没有新的合成节拍（窗口被遮挡或最小化）：别空转刷帧。
                    Thread.Sleep(8);
                    continue;
                }

                int index = (int)swapChain3.CurrentBackBufferIndex;
                resources[index] ??= createFrameResources(d2dFactory, swapChain, index);

                double nowMs = frameClock.Elapsed.TotalMilliseconds;
                double delta = Math.Clamp(nowMs - lastFrame, 0, 120);

                ManiaPreviewChart? snapshot;
                double timeMs;
                lock (gate)
                {
                    clock.Advance(nowMs, delta);
                    timeMs = clock.Time;
                    snapshot = chart;
                }

                draw(resources[index]!, width, height, snapshot, timeMs);
                SharpGen.Runtime.Result result = swapChain.Present(1, PresentFlags.None);
                if (result.Failure && !presentErrorLogged)
                {
                    presentErrorLogged = true;
                    AppLog.Info($"预览渲染管线：Present 失败 {result.Description}（不再重复记录）");
                }

                string? pendingCapture;
                lock (gate)
                {
                    pendingCapture = captureRequest;
                    captureRequest = null;
                }
                if (pendingCapture is not null)
                    saveCurrentFrame(device, swapChain, index, pendingCapture);

                double finishedAt = frameClock.Elapsed.TotalMilliseconds;
                LastDrawMilliseconds = finishedAt - nowMs;
                // 记录的是**帧间隔**（含等待节拍的那段），这才是眼睛看到的节奏；
                // 绘制耗时单独记在 LastDrawMilliseconds。
                double interval = nowMs - lastFrame;
                lastFrame = nowMs;
                lock (gate)
                {
                    if (interval > 0)
                        frames.Add(interval);
                    if (frames.Count > 900)
                        frames.RemoveRange(0, frames.Count - 900);
                }

                if (frameLatency == IntPtr.Zero)
                    Thread.Sleep(1);
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("预览渲染线程异常退出", exception);
        }
        finally
        {
            d2dFactory?.Dispose();
            swapChain3?.Dispose();
            swapChain2?.Dispose();
            swapChain?.Dispose();
            context?.Dispose();
            device?.Dispose();
        }
    }


    /// <summary>把交换链当前缓冲区读回内存并写 PNG（开发用）。</summary>
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
                {
                    IntPtr source = map.DataPointer + row * stride;
                    System.Runtime.InteropServices.Marshal.Copy(source, pixels, row * width * 4, width * 4);
                }

                System.Windows.Media.Imaging.BitmapSource bitmap =
                    System.Windows.Media.Imaging.BitmapSource.Create(width, height, 96, 96,
                        System.Windows.Media.PixelFormats.Bgr32, null, pixels, width * 4);

                System.Windows.Media.Imaging.PngBitmapEncoder encoder = new();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using FileStream stream = File.Create(path);
                encoder.Save(stream);
                AppLog.Info($"预览渲染管线：已读回一帧 {width}x{height} -> {path}");
            }
            finally
            {
                context.Unmap(staging, 0);
            }
        }
        catch (Exception exception)
        {
            AppLog.Error($"预览渲染管线：读回当前帧失败 {path}", exception);
        }
    }

    private static FrameResources createFrameResources(ID2D1Factory1 factory, IDXGISwapChain1 swapChain, int index)
    {
        // 翻转模型的缓冲区会轮转，必须按当前索引取，不能一直取 0（否则画到前面那块上，屏幕全黑）。
        IDXGISurface surface = swapChain.GetBuffer<IDXGISurface>((uint)index);
        RenderTargetProperties properties = new(
            RenderTargetType.Default,
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore),
            96, 96,
            RenderTargetUsage.None,
            Vortice.Direct2D1.FeatureLevel.Default);

        ID2D1RenderTarget target = factory.CreateDxgiSurfaceRenderTarget(surface, properties);
        return new FrameResources
        {
            Surface = surface,
            Target = target,
            Lane = target.CreateSolidColorBrush(new Color4(0x0F / 255f, 0x14 / 255f, 0x18 / 255f, 1f)),
            LaneAlt = target.CreateSolidColorBrush(new Color4(0x14 / 255f, 0x1A / 255f, 0x1F / 255f, 1f)),
            Tap = target.CreateSolidColorBrush(new Color4(0xF5 / 255f, 0xF9 / 255f, 0xFB / 255f, 1f)),
            Hold = target.CreateSolidColorBrush(new Color4(0xE1 / 255f, 0x51 / 255f, 0x7B / 255f, 1f)),
            HoldHead = target.CreateSolidColorBrush(new Color4(0xFB / 255f, 0xF2 / 255f, 0xF5 / 255f, 1f)),
            Judgement = target.CreateSolidColorBrush(new Color4(0x5A / 255f, 0x6A / 255f, 0x76 / 255f, 1f)),
            KeyBand = target.CreateSolidColorBrush(new Color4(0x18 / 255f, 0x1F / 255f, 0x25 / 255f, 1f))
        };
    }

    private static D2DRect rect(double x, double y, double width, double height) =>
        new((float)x, (float)y, (float)Math.Max(0, width), (float)Math.Max(0, height));

    private void draw(FrameResources resources, int width, int height, ManiaPreviewChart? chart, double timeMs)
    {
        ID2D1RenderTarget target = resources.Target;
        // Direct2D 的绘制是批处理的：不包在 BeginDraw/EndDraw 里，命令根本不会提交给 GPU
        //（表现为交换链永远是黑的，而且耗时只有 0.0x 毫秒 —— 因为什么都没画）。
        target.BeginDraw();
        target.Clear(new Color4(0.043f, 0.055f, 0.067f, 1f));
        DrawnNotes = 0;
        if (chart is null || width <= 1 || height <= 1)
        {
            target.EndDraw();
            return;
        }

        int keys = chart.KeyCount;
        double laneWidth = (double)width / keys;
        double judgementY = Math.Max(KeyBandHeight + 40, height - KeyBandHeight);
        double pixelsPerMs = judgementY / Math.Max(0.25, VisibleSeconds) / 1000.0;

        for (int key = 0; key < keys; key++)
        {
            double x = key * laneWidth;
            target.FillRectangle(rect(x, 0, laneWidth, judgementY), (key & 1) == 1 ? resources.LaneAlt : resources.Lane);
        }

        double noteHeight = Math.Clamp(laneWidth * 0.17, 7, 20);
        double noteWidth = Math.Max(2, laneWidth - 6);
        IReadOnlyList<PreviewNote> notes = chart.Notes;
        double visibleMs = VisibleSeconds * 1000;
        int drawn = 0;

        for (int index = chart.FirstIndexAt(timeMs - chart.LongestHold - 1); index < notes.Count; index++)
        {
            PreviewNote note = notes[index];
            if (note.Start - timeMs > visibleMs)
                break;
            if (note.End < timeMs)
                continue;

            double headY = judgementY - (note.Start - timeMs) * pixelsPerMs;
            double tailY = judgementY - (note.End - timeMs) * pixelsPerMs;
            double x = note.Column * laneWidth + 3;

            if (note.IsHold && headY - tailY > 0.5)
            {
                double bodyTop = Math.Max(tailY, 0);
                double bodyBottom = Math.Min(headY, judgementY);
                if (bodyBottom - bodyTop > 0.5)
                    target.FillRectangle(rect(x, bodyTop, noteWidth, bodyBottom - bodyTop), resources.Hold);

                if (headY >= 0 && headY < judgementY)
                    target.FillRectangle(rect(x, headY, noteWidth, Math.Min(noteHeight, judgementY - headY)), resources.HoldHead);
                if (tailY >= 0 && tailY < judgementY)
                    target.FillRectangle(rect(x, tailY, noteWidth, Math.Min(noteHeight, 4)), resources.HoldHead);
            }
            else
            {
                double y = Math.Max(headY, 0);
                if (y >= judgementY)
                    continue;
                target.FillRectangle(rect(x, y, noteWidth, Math.Min(noteHeight, judgementY - y)), resources.Tap);
            }

            drawn++;
        }

        DrawnNotes = drawn;
        target.FillRectangle(rect(0, judgementY - 2, width, 2), resources.Judgement);
        target.FillRectangle(rect(0, judgementY, width, KeyBandHeight), resources.KeyBand);

        SharpGen.Runtime.Result endResult = target.EndDraw();
        if (endResult.Failure && !endDrawErrorLogged)
        {
            endDrawErrorLogged = true;
            AppLog.Info($"预览渲染管线：EndDraw 失败 {endResult.Description}（不再重复记录）");
        }
    }

    private bool endDrawErrorLogged;

    private const uint WaitTimeout = 258;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName,
        WindowStyles style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, SetWindowPosFlags flags);

    [Flags]
    private enum WindowStyles : uint
    {
        WS_CHILD = 0x40000000,
        WS_VISIBLE = 0x10000000,
        WS_CLIPSIBLINGS = 0x04000000
    }

    [Flags]
    private enum SetWindowPosFlags : uint
    {
        SWP_NOZORDER = 0x0004,
        SWP_NOACTIVATE = 0x0010
    }
}
