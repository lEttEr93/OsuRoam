using System.Diagnostics;
using OsuRoam.Services;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Color4 = Vortice.Mathematics.Color4;

namespace OsuRoam.Controls;

public sealed partial class PreviewOverlay
{
    // Each note is geometry referencing a one-pixel opaque surface. There is no viewport-sized
    // transparent content buffer. The compositor owns movement, clipping and subtree opacity.
    private sealed class NoteScene : ICompositionScene
    {
        private readonly IDCompositionDevice composition;
        private readonly ManiaPreviewChart chart;
        private readonly int width, height;
        private readonly double span, pixelsPerMs;
        private readonly List<IDisposable> resources = [];
        private readonly List<IDCompositionVisual> bands = [];
        private readonly Dictionary<int, List<IDisposable>> notes = [];
        private readonly IDCompositionSurface tap, hold, head;
        private readonly IDCompositionTranslateTransform scroll;
        private readonly IDCompositionEffectGroup opacityEffect;
        private readonly PreviewMotion motion = new();
        private double origin = double.NaN;
        public IDCompositionVisual Root { get; }
        public int CachedNotes => notes.Count;
        public bool ProbeCommitted { get; set; }

        private T Own<T>(T value) where T : IDisposable { resources.Add(value); return value; }

        public NoteScene(IDCompositionDevice composition, ID2D1Factory1 factory,
            ManiaPreviewChart chart, int width, int height, double seconds)
        {
            this.composition = composition;
            this.chart = chart;
            this.width = width;
            this.height = height;
            span = seconds * 1000;
            pixelsPerMs = height / span;
            try
            {
                Root = Own(composition.CreateVisual());
                Root.SetClip(new RawRectF(0, 0, width, height)).CheckError();
                Root.SetBitmapInterpolationMode(Vortice.DirectComposition.BitmapInterpolationMode.Linear).CheckError();
                scroll = Own(composition.CreateTranslateTransform());
                opacityEffect = Own(composition.CreateEffectGroup());
                Root.SetEffect(opacityEffect).CheckError();
                tap = Solid(new Color4(0.961f, 0.976f, 0.984f, 1));
                hold = Solid(new Color4(0.882f, 0.318f, 0.482f, 1));
                head = Solid(new Color4(0.984f, 0.949f, 0.961f, 1));
                float fadeHeight = Math.Max(24, height * 0.18f);
                for (int i = 0; i <= 24; i++)
                {
                    IDCompositionVisual band = Own(composition.CreateVisual());
                    float top = fadeHeight * i / 24;
                    float bottom = i < 24 ? fadeHeight * (i + 1) / 24 : height;
                    band.SetClip(new RawRectF(0, top, width, bottom)).CheckError();
                    if (i < 24)
                    {
                        IDCompositionEffectGroup fade = Own(composition.CreateEffectGroup());
                        fade.SetOpacity((i + 0.5f) / 24).CheckError();
                        band.SetEffect(fade).CheckError();
                    }
                    Root.AddVisual(band, true, null).CheckError();
                    bands.Add(band);
                }
                IDCompositionSurface line = Solid(new Color4(0.353f, 0.416f, 0.463f, 1));
                AddRectangle(Root, line, 0, height - 2.5, width, 1.5, resources, false);
            }
            catch { Dispose(); throw; }

            IDCompositionSurface Solid(Color4 color)
            {
                composition.CreateSurface(1, 1, Format.B8G8R8A8_UNorm, Vortice.DXGI.AlphaMode.Ignore,
                    out IDCompositionSurface surface).CheckError();
                Own(surface);
                using IDXGISurface dxgi = surface.BeginDraw<IDXGISurface>(null, out Vortice.Mathematics.Int2 offset);
                try
                {
                    using ID2D1RenderTarget target = factory.CreateDxgiSurfaceRenderTarget(dxgi,
                        new RenderTargetProperties(RenderTargetType.Default,
                            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore),
                            96, 96, RenderTargetUsage.None, Vortice.Direct2D1.FeatureLevel.Default));
                    target.BeginDraw();
                    target.Transform = System.Numerics.Matrix3x2.CreateTranslation(offset.X, offset.Y);
                    target.PushAxisAlignedClip(rect(0, 0, 1, 1), AntialiasMode.Aliased);
                    target.Clear(color);
                    target.PopAxisAlignedClip();
                    target.EndDraw().CheckError();
                }
                finally { surface.EndDraw().CheckError(); }
                return surface;
            }
        }

        private void AddRectangle(IDCompositionVisual parent, IDCompositionSurface surface,
            double x, double y, double w, double h, List<IDisposable> owner, bool moving = true)
        {
            if (h <= 0 || w <= 0) return;
            IDCompositionVisual visual = composition.CreateVisual();
            owner.Add(visual);
            IDCompositionScaleTransform scale = composition.CreateScaleTransform();
            owner.Add(scale);
            scale.SetScaleX((float)w).CheckError();
            scale.SetScaleY((float)h).CheckError();
            visual.SetContent(surface).CheckError();
            visual.SetTransform(scale).CheckError();
            visual.SetOffsetX((float)x).CheckError();
            visual.SetOffsetY((float)y).CheckError();
            IDCompositionVisual wrapper = composition.CreateVisual();
            owner.Add(wrapper);
            if (moving) wrapper.SetTransform(scroll).CheckError();
            wrapper.AddVisual(visual, true, null).CheckError();
            parent.AddVisual(wrapper, true, null).CheckError();
        }

        private void ClearNotes()
        {
            foreach (IDCompositionVisual band in bands) band.RemoveAllVisuals().CheckError();
            foreach (List<IDisposable> list in notes.Values)
                for (int i = list.Count - 1; i >= 0; i--) list[i].Dispose();
            notes.Clear();
        }

        public int Update(double time, double rate, double remaining, long qpc, double opacity, bool seek)
        {
            motion.Update((double)qpc / Stopwatch.Frequency, time, rate, remaining, seek);
            time = motion.Position;
            if (seek || double.IsNaN(origin) || Math.Abs(time - origin) > span * 32)
            {
                ClearNotes();
                origin = time;
            }
            int changes = 0;
            foreach (int index in notes.Keys.ToArray())
            {
                if (chart.Notes[index].End >= time - 60) continue;
                List<IDisposable> list = notes[index];
                // Each rectangle contributes visual, scale, wrapper in this order.
                for (int n = 2, band = 0; n < list.Count; n += 3, band = (band + 1) % bands.Count)
                    bands[band].RemoveVisual((IDCompositionVisual)list[n]).CheckError();
                for (int n = list.Count - 1; n >= 0; n--) list[n].Dispose();
                notes.Remove(index);
                changes++;
            }
            double lane = (double)width / chart.KeyCount;
            double noteHeight = Math.Clamp(lane * 0.17, 6, 18);
            for (int index = chart.FirstIndexAt(time - chart.LongestHold - 60); index < chart.Notes.Count; index++)
            {
                PreviewNote note = chart.Notes[index];
                if (note.Start > time + span * 2) break;
                if (note.End < time - 60 || notes.ContainsKey(index)) continue;
                List<IDisposable> list = [];
                notes.Add(index, list);
                double x = note.Column * lane + 3;
                double y = height - (note.Start - origin) * pixelsPerMs;
                double tail = height - (note.End - origin) * pixelsPerMs;
                if (note.IsHold)
                    foreach (IDCompositionVisual band in bands)
                        AddRectangle(band, hold, x, tail, Math.Max(2, lane - 6), y - tail, list);
                foreach (IDCompositionVisual band in bands)
                    AddRectangle(band, note.IsHold ? head : tap, x, y, Math.Max(2, lane - 6), noteHeight, list);
                changes++;
            }
            opacityEffect.SetOpacity((float)opacity).CheckError();
            using IDCompositionAnimation animation = composition.CreateAnimation();
            animation.SetAbsoluteBeginTime(qpc).CheckError();
            float start = (float)((time - origin) * pixelsPerMs);
            double duration = remaining / 1000;
            if (duration > 0)
            {
                float v = (float)(motion.Velocity * pixelsPerMs);
                float a = (float)(motion.Acceleration * pixelsPerMs);
                double blend = motion.Blend;
                float end = start + v * (float)blend + a * (float)(blend * blend);
                float endV = (float)(motion.EndVelocity * pixelsPerMs);
                animation.AddCubic(0, start, v, a, 0).CheckError();
                if (duration > blend) animation.AddCubic(blend, end, endV, 0, 0).CheckError();
                animation.End(duration, end + endV * (float)(duration - blend)).CheckError();
                scroll.SetOffsetY(animation).CheckError();
            }
            else scroll.SetOffsetY(start).CheckError();
            return changes;
        }

        public void Dispose()
        {
            ClearNotes();
            for (int i = resources.Count - 1; i >= 0; i--) resources[i].Dispose();
            resources.Clear();
        }
    }
}
