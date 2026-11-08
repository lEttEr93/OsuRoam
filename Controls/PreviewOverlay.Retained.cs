using System.Diagnostics;
using System.Numerics;
using OsuRoam.Services;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Color4 = Vortice.Mathematics.Color4;

namespace OsuRoam.Controls;

public sealed partial class PreviewOverlay
{
    /// <summary>
    /// No per-frame Present: three cached chart surfaces are moved by one shared DComp
    /// animation. The worker only maintains the audio clock, replenishes tiles and handles
    /// lifecycle changes. Fixed opacity bands mask the top without painting over the desktop.
    /// </summary>
    private void renderRetainedLoop(CancellationToken token)
    {
        try
        {
            using IDXGIAdapter? adapter = pickAdapter();
            D3D11.D3D11CreateDevice(adapter, adapter is null ? DriverType.Hardware : DriverType.Unknown,
                DeviceCreationFlags.BgraSupport,
                [Vortice.Direct3D.FeatureLevel.Level_11_0, Vortice.Direct3D.FeatureLevel.Level_10_1],
                out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
            using (device)
            using (context)
            using (IDXGIDevice dxgi = device.QueryInterface<IDXGIDevice>())
            using (IDCompositionDevice composition = DComp.DCompositionCreateDevice3<IDCompositionDevice>(dxgi))
            using (IDCompositionDesktopDevice desktop = composition.QueryInterface<IDCompositionDesktopDevice>())
            using (ID2D1Factory1 factory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded))
            {
                desktop.CreateTargetForHwnd(window, true, out IDCompositionTarget target).CheckError();
                using (target)
                {
                    using (IDXGIAdapter actualAdapter = dxgi.GetAdapter())
                        AppLog.Info($"预览：合成器滚动，适配器 {actualAdapter.Description.Description.Trim()}");
                    ICompositionScene? scene = null;
                    ManiaPreviewChart? activeChart = null;
                    int width = 0, height = 0;
                    double seconds = 0;
                    int discontinuity = -1;
                    double previousNow = now.Elapsed.TotalMilliseconds;
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            long qpc = Stopwatch.GetTimestamp();
                            double currentNow = now.Elapsed.TotalMilliseconds;
                            ManiaPreviewChart? currentChart;
                            double time, rate, remaining, currentSeconds, opacity;
                            int currentDiscontinuity, currentWidth, currentHeight;
                            string? capture;
                            lock (gate)
                            {
                                clock.Advance(currentNow, currentNow - previousNow);
                                time = clock.Time + LatencyMilliseconds;
                                rate = clock.Rate;
                                remaining = clock.PredictionRemaining(currentNow);
                                currentDiscontinuity = clock.Discontinuity;
                                currentChart = chart;
                                currentSeconds = double.IsFinite(VisibleSeconds) ? Math.Clamp(VisibleSeconds, 0.25, 10) : 1.25;
                                opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0, 1) : 1;
                                currentWidth = pixelWidth;
                                currentHeight = pixelHeight;
                                capture = captureRequest;
                                captureRequest = null;
                            }
                            previousNow = currentNow;
                            if (currentChart is null || currentWidth <= 1 || currentHeight <= 1)
                            {
                                token.WaitHandle.WaitOne(50);
                                continue;
                            }

                            if (scene is null || currentChart != activeChart || currentWidth != width ||
                                currentHeight != height || currentSeconds != seconds)
                            {
                                // Replace the complete visual tree in one commit. Never resize a surface
                                // that DWM might still be sampling, or expose half-painted content.
                                ICompositionScene replacement = NoteVisualMode
                                    ? new NoteScene(composition, factory, currentChart, currentWidth, currentHeight, currentSeconds)
                                    : new RetainedScene(composition, factory, currentChart,
                                        currentWidth, currentHeight, currentSeconds, time);
                                target.SetRoot(replacement.Root).CheckError();
                                ICompositionScene? old = scene;
                                scene = replacement;
                                activeChart = currentChart;
                                width = currentWidth;
                                height = currentHeight;
                                seconds = currentSeconds;
                                old?.Dispose();
                                TileUpdates += 3;
                                RendererDescription = NoteVisualMode ? "DComp note visuals (opaque primitives)" : "DComp retained tiles (compositor animation)";
                                UsesRetainedRenderer = true;
                                AppLog.Info($"预览：内容块就绪 {width}x{height}, {currentChart.KeyCount}K, {seconds:0.00}s/屏");
                            }

                            int updates = scene.Update(time, rate, remaining, qpc, opacity,
                                discontinuity != currentDiscontinuity);
                            TileUpdates += updates;
                            discontinuity = currentDiscontinuity;
                            if (!UncorrectedMotionProbe || updates > 0 || !scene.ProbeCommitted)
                            {
                                composition.Commit().CheckError();
                                scene.ProbeCommitted = true;
                                AnimationCommits++;
                            }
                            DrawnNotes = scene.CachedNotes;
                            if (capture is not null)
                                captureRetainedReference(device, factory, currentChart, width, height,
                                    time, seconds, opacity, capture);

                            // A 20Hz maintenance tick does not set the visual frame rate. DWM evaluates
                            // the polynomial at each composition frame; no high resolution timer needed.
                            token.WaitHandle.WaitOne(50);
                        }
                    }
                    finally
                    {
                        target.SetRoot(null);
                        composition.Commit();
                        scene?.Dispose();
                    }
                }
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("预览：合成器滚动失败，回退交换链渲染", exception);
            if (!token.IsCancellationRequested)
                renderLoop(token);
        }
    }

    private interface ICompositionScene : IDisposable
    {
        IDCompositionVisual Root { get; }
        int CachedNotes { get; }
        bool ProbeCommitted { get; set; }
        int Update(double time, double rate, double remaining, long qpc, double opacity, bool seek);
    }

    private sealed class RetainedScene : ICompositionScene
    {
        private readonly IDCompositionDevice composition;
        private readonly List<IDisposable> resources = [];
        private T Own<T>(T resource) where T : IDisposable
        {
            resources.Add(resource);
            return resource;
        }
        private readonly ID2D1Factory1 factory;
        private readonly ManiaPreviewChart chart;
        private readonly int width, height;
        private readonly double span, pixelsPerMs;
        private readonly List<IDCompositionVisual> bands = [];
        private readonly List<IDCompositionEffectGroup> bandEffects = [];
        private readonly Tile[] tiles = new Tile[3];
        private readonly IDCompositionTranslateTransform scroll;
        private readonly IDCompositionEffectGroup opacityEffect;
        private readonly IDCompositionSurface judgement;
        private readonly IDCompositionVisual judgementVisual;
        private double origin;
        private readonly PreviewMotion motion = new();
        public bool ProbeCommitted { get; set; }
        private bool probeAnimation;

        public IDCompositionVisual Root { get; }
        public int CachedNotes => tiles.Sum(tile => tile.NoteCount);

        public RetainedScene(IDCompositionDevice composition, ID2D1Factory1 factory,
            ManiaPreviewChart chart, int width, int height, double seconds, double time)
        {
            this.composition = composition;
            this.factory = factory;
            this.chart = chart;
            this.width = width;
            this.height = height;
            span = seconds * 1000;
            pixelsPerMs = height / span;
            origin = Math.Floor(time / span) * span;
            try
            {
                Root = Own(composition.CreateVisual());
                Root.SetClip(new RawRectF(0, 0, width, height)).CheckError();
                opacityEffect = Own(composition.CreateEffectGroup());
                Root.SetEffect(opacityEffect).CheckError();
                scroll = Own(composition.CreateTranslateTransform());

                // Pixel-aligned, disjoint clips prevent seams and double blending. 2px bands
                // are finer than the previous renderer's 6px fade and stay in viewport space.
                int fadeHeight = Math.Max(24, (int)Math.Round(height * 0.18));
                for (int top = 0; top < height;)
                {
                    int bottom = top < fadeHeight ? Math.Min(top + 2, fadeHeight) : height;
                    IDCompositionVisual band = Own(composition.CreateVisual());
                    band.SetClip(new RawRectF(0, top, width, bottom)).CheckError();
                    if (top < fadeHeight)
                    {
                        IDCompositionEffectGroup fade = Own(composition.CreateEffectGroup());
                        fade.SetOpacity((top + bottom) * 0.5f / fadeHeight).CheckError();
                        band.SetEffect(fade).CheckError();
                        bandEffects.Add(fade);
                    }
                    Root.AddVisual(band, true, null).CheckError();
                    bands.Add(band);
                    top = bottom;
                }

                long firstTile = (long)Math.Floor(time / span);
                for (int i = 0; i < tiles.Length; i++)
                    tiles[i] = CreateTile(firstTile + i);

                judgement = CreateSurface(width, 3);
                Paint(judgement, width, 3, null, target =>
                {
                    using ID2D1SolidColorBrush brush = target.CreateSolidColorBrush(new Color4(0.353f, 0.416f, 0.463f, 1));
                    target.FillRectangle(rect(0, 0.5, width, 1.5), brush);
                });
                judgementVisual = Own(composition.CreateVisual());
                judgementVisual.SetContent(judgement).CheckError();
                judgementVisual.SetOffsetY(height - 3).CheckError();
                Root.AddVisual(judgementVisual, true, null).CheckError();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public int Update(double time, double rate, double remaining, long qpc, double opacity, bool seek)
        {
            motion.Update((double)qpc / Stopwatch.Frequency, time, rate, remaining, seek);
            time = motion.Position;
            long first = (long)Math.Floor(time / span);
            int updates = 0;
            // Large positions lose subpixel precision in floats; keep offsets near the viewport.
            if (seek || Math.Abs(time - origin) > span * 32)
                origin = first * span;
            HashSet<long> needed = [first, first + 1, first + 2];
            foreach (Tile tile in tiles)
                needed.Remove(tile.Index);
            foreach (Tile tile in tiles)
            {
                if (tile.Index >= first && tile.Index <= first + 2)
                    continue;
                long index = needed.Min();
                needed.Remove(index);
                PaintTile(tile, index);
                updates++;
            }
            foreach (Tile tile in tiles)
            {
                float offset = (float)(height + (origin - (tile.Index + 1) * span) * pixelsPerMs);
                foreach (IDCompositionVisual visual in tile.Visuals)
                    visual.SetOffsetY(offset).CheckError();
            }

            opacityEffect.SetOpacity((float)opacity).CheckError();
            if (UncorrectedMotionProbe)
            {
                if (!probeAnimation)
                {
                    using IDCompositionAnimation animation = composition.CreateAnimation();
                    animation.SetAbsoluteBeginTime(qpc).CheckError();
                    float position = (float)((time - origin) * pixelsPerMs);
                    animation.AddCubic(0, position, (float)(pixelsPerMs * 1000), 0, 0).CheckError();
                    animation.End(86400, position + (float)(pixelsPerMs * 86400000)).CheckError();
                    scroll.SetOffsetY(animation).CheckError();
                    probeAnimation = true;
                }
                return updates;
            }
            double duration = remaining / 1000;
            float start = (float)((time - origin) * pixelsPerMs);
            float velocity = (float)(motion.Velocity * pixelsPerMs);
            if (duration > 0)
            {
                using IDCompositionAnimation animation = composition.CreateAnimation();
                animation.SetAbsoluteBeginTime(qpc).CheckError();
                // AddCubic(beginOffset, constant, linear, quadratic, cubic).
                float acceleration = (float)(motion.Acceleration * pixelsPerMs);
                double blend = motion.Blend;
                float end = start + velocity * (float)blend + acceleration * (float)(blend * blend);
                float endVelocity = (float)(motion.EndVelocity * pixelsPerMs);
                animation.AddCubic(0, start, velocity, acceleration, 0).CheckError();
                if (duration > blend)
                    animation.AddCubic(blend, end, endVelocity, 0, 0).CheckError();
                animation.End(duration, end + endVelocity * (float)(duration - blend)).CheckError();
                scroll.SetOffsetY(animation).CheckError();
            }
            else
                scroll.SetOffsetY(start).CheckError();
            return updates;
        }

        private IDCompositionSurface CreateSurface(int w, int h)
        {
            composition.CreateSurface((uint)w, (uint)h, Format.B8G8R8A8_UNorm,
                Vortice.DXGI.AlphaMode.Premultiplied, out IDCompositionSurface surface).CheckError();
            return Own(surface);
        }


        private PreviewSkin? skin;
        private bool loggedFirstTile;

        private PreviewSkin loadSkin(ID2D1RenderTarget target)
        {
            PreviewSkin loaded = new();
            loaded.Load(target, PreviewSkin.ResolveDirectory(SkinDirectory), chart.KeyCount);
            return loaded;
        }

        private ID2D1BitmapBrush? createBodyBrush(ID2D1RenderTarget target)
        {
            if (skin?.HoldBody is null)
                return null;

            // 横向拉伸（左右描边不糊），纵向平铺 —— 长条怎么拉长都保持图案密度。
            ID2D1BitmapBrush brush = target.CreateBitmapBrush(skin.HoldBody,
                new Vortice.Direct2D1.BitmapBrushProperties(ExtendMode.Clamp, ExtendMode.Wrap,
                    Vortice.Direct2D1.BitmapInterpolationMode.Linear));
            return brush;
        }

        private static void drawBitmap(ID2D1RenderTarget target, ID2D1Bitmap bitmap,
            double x, double y, double width, double height)
        {
            // 源矩形是**位图像素**，不是目标尺寸。之前这里传的是目标宽高，
            // 等于只取了位图左上角一块 —— 表现就是“只有左半截”。
            Vortice.Mathematics.SizeI size = bitmap.PixelSize;
            target.DrawBitmap(bitmap, rect(x, y, width, height), 1f,
                Vortice.Direct2D1.BitmapInterpolationMode.Linear,
                rect(0, 0, size.Width, size.Height));
        }

        private Tile CreateTile(long index)
        {
            Tile tile = new() { Surface = CreateSurface(width, height) };
            PaintTile(tile, index);
            foreach (IDCompositionVisual band in bands)
            {
                IDCompositionVisual visual = Own(composition.CreateVisual());
                visual.SetContent(tile.Surface).CheckError();
                visual.SetTransform(scroll).CheckError();
                band.AddVisual(visual, true, null).CheckError();
                tile.Visuals.Add(visual);
            }
            return tile;
        }

        private void PaintTile(Tile tile, long index)
        {
            tile.Index = index;
            double start = index * span, end = start + span;
            double lane = (double)width / chart.KeyCount;
            double noteHeight = Math.Clamp(lane * 0.17, 6, 18);
            int count = 0;
            ID2D1BitmapBrush? bodyBrush = null;
            try
            {
            Paint(tile.Surface, width, height,
                target =>
                {
                    // 位图和位图画刷都必须在 BeginDraw **之前**创建：
                    // D2D 在绘制期间持设备锁，那时 CreateBitmap/CreateBitmapBrush 会死锁。
                    skin ??= loadSkin(target);
                    bodyBrush = createBodyBrush(target);
                },
                target =>
            {
                using ID2D1SolidColorBrush tap = target.CreateSolidColorBrush(new Color4(0.961f, 0.976f, 0.984f, 1));
                using ID2D1SolidColorBrush hold = target.CreateSolidColorBrush(new Color4(0.882f, 0.318f, 0.482f, 1));
                using ID2D1SolidColorBrush head = target.CreateSolidColorBrush(new Color4(0.984f, 0.949f, 0.961f, 1));
                for (int i = chart.FirstIndexAt(start - chart.LongestHold); i < chart.Notes.Count; i++)
                {
                    PreviewNote note = chart.Notes[i];
                    if (note.Start > end + noteHeight / pixelsPerMs)
                        break;
                    if (note.End < start)
                        continue;
                    double x = note.Column * lane + 3;
                    double y = (end - note.Start) * pixelsPerMs;
                    double tail = (end - note.End) * pixelsPerMs;
                    double noteWidth = Math.Max(2, lane - 6);
                    if (note.IsHold)
                    {
                        if (bodyBrush is not null && skin?.HoldBody is not null)
                            target.FillRectangle(rect(x, tail, noteWidth, y - tail), bodyBrush);
                        else
                            target.FillRectangle(rect(x, tail, noteWidth, y - tail), hold);

                        if (skin?.HoldHead is not null)
                            drawBitmap(target, skin.HoldHead, x, y, noteWidth, noteHeight);
                        else
                            target.FillRectangle(rect(x, y, noteWidth, noteHeight), head);
                    }
                    else if (skin?.Note is not null)
                    {
                        drawBitmap(target, skin.Note, x, y, noteWidth, noteHeight);
                    }
                    else
                    {
                        target.FillRectangle(rect(x, y, noteWidth, noteHeight), tap);
                    }
                    count++;
                }
            });
            }
            finally
            {
                bodyBrush?.Dispose();
            }
            tile.NoteCount = count;
            if (!loggedFirstTile)
            {
                loggedFirstTile = true;
                AppLog.Info($"预览：第一块图块已绘制（{count} 个音符，{width}x{height}）");
            }
        }

        private void Paint(IDCompositionSurface surface, int w, int h,
            Action<ID2D1RenderTarget>? prepare, Action<ID2D1RenderTarget> draw)
        {
            using IDXGISurface dxgiSurface = surface.BeginDraw<IDXGISurface>(null,
                out Vortice.Mathematics.Int2 offset);
            try
            {
                using ID2D1RenderTarget target = factory.CreateDxgiSurfaceRenderTarget(dxgiSurface,
                    new RenderTargetProperties(RenderTargetType.Default,
                        new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                        96, 96, RenderTargetUsage.None, Vortice.Direct2D1.FeatureLevel.Default));
                // 皮肤位图必须在 BeginDraw **之前**创建：D2D 在绘制期间持有设备锁，
                // 那时调 CreateBitmap 会直接死锁（现场：日志停在「预览贴图：… 载入 6 张」然后卡死）。
                prepare?.Invoke(target);
                target.BeginDraw();
                target.Transform = Matrix3x2.CreateTranslation(offset.X, offset.Y);
                target.AntialiasMode = AntialiasMode.Aliased;
                target.PushAxisAlignedClip(rect(0, 0, w, h), AntialiasMode.Aliased);
                target.Clear(new Color4(0, 0, 0, 0));
                draw(target);
                target.PopAxisAlignedClip();
                target.EndDraw().CheckError();
            }
            finally
            {
                surface.EndDraw().CheckError();
            }
        }

        public void Dispose()
        {
            for (int i = resources.Count - 1; i >= 0; i--)
                resources[i].Dispose();
            resources.Clear();
        }

        private sealed class Tile
        {
            public required IDCompositionSurface Surface { get; init; }
            public List<IDCompositionVisual> Visuals { get; } = [];
            public long Index { get; set; }
            public int NoteCount { get; set; }
        }
    }

    // A transparent reference frame for visual diagnostics, not a DWM screen capture.
    // The retained renderer has no swap chain to read back. Reuse the reference rasterizer
    // only when explicitly requested; it does not participate in normal rendering.
    private void captureRetainedReference(ID3D11Device device, ID2D1Factory1 factory,
        ManiaPreviewChart chart, int width, int height, double time, double seconds, double opacity, string path)
    {
        using IDXGIDevice dxgi = device.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = dxgi.GetAdapter();
        using IDXGIFactory2 dxgiFactory = adapter.GetParent<IDXGIFactory2>();
        using IDXGISwapChain1 reference = dxgiFactory.CreateSwapChainForComposition(device, new SwapChainDescription1
        {
            Width = (uint)width, Height = (uint)height, Format = Format.B8G8R8A8_UNorm,
            BufferCount = 2, BufferUsage = Usage.RenderTargetOutput,
            SampleDescription = new SampleDescription(1, 0), SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied
        });
        using FrameResources frame = createFrameResources(factory, reference, 0);
        draw(frame, width, height, chart, time, seconds, opacity);
        saveCurrentFrame(device, reference, 0, path);
        AppLog.Info("预览：已保存参考栅格帧（不是 DWM 上屏捕获）");
    }
}
