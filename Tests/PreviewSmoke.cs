using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using OsuRoam.Controls;
using OsuRoam.Services;

// Explicit opt-in, on-device graphics integration test. No game, tosu, credentials or network.
internal static class PreviewSmoke
{
    public static int Run(string? reportPath, bool steady = false, bool opaque = false, bool hwndAlpha = false, bool heartbeat = false, bool unpaced = false, bool noteVisual = false)
    {
        Exception? failure = null;
        StringBuilder report = new();
        Thread thread = new(() =>
        {
            try
            {
                PreviewOverlay.UncorrectedMotionProbe = steady;
                PreviewOverlay.NoteVisualMode = noteVisual;
                PreviewOverlay.OpaqueMode = opaque;
                PreviewOverlay.LegacyMode = opaque || unpaced;
                PreviewOverlay.PaceMode = unpaced ? 3 : 0;
                PreviewOverlay.TransparentHwndMode = hwndAlpha;
                Window owner = new()
                {
                    Title = "osu! Roam preview graphics check",
                    Width = 850, Height = 560, Left = 100, Top = 100,
                    Background = System.Windows.Media.Brushes.DarkSlateGray,
                    Content = "Preview: 4K / 7K / 10K\nresize / speed / seek / pause / reopen",
                    Topmost = true
                };
                owner.Show();
                using PreviewOverlay? beat = heartbeat ? CreateHeartbeat(owner) : null;
                using PreviewOverlay overlay = new();
                overlay.SetOwner(new WindowInteropHelper(owner).Handle);
                overlay.SetBounds(650, 150, 452, 632);
                ManiaPreviewChart[] charts = [BuildChart(4), BuildChart(7), BuildChart(10)];
                if (steady) overlay.Sync(10000);
                overlay.Show(charts[0]);
                Stopwatch elapsed = Stopwatch.StartNew();
                int stage = -1;
                DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
                timer.Tick += (_, _) =>
                {
                    try
                    {
                        double seconds = elapsed.Elapsed.TotalSeconds;
                        int next = (int)(seconds / 3);
                        if (next != stage)
                        {
                            stage = next;
                            switch (steady ? -1 : stage)
                            {
                                case 1: overlay.Show(charts[1]); break;
                                case 2: overlay.Show(charts[2]); break;
                                case 3: overlay.SetBounds(650, 150, 360, 510); overlay.VisibleSeconds = 0.75; break;
                                case 4: overlay.VisibleSeconds = 2; overlay.Opacity = 0.65; break;
                                case 5: overlay.Opacity = 1; break; // backwards seek below
                                case 7: overlay.Hide(); overlay.Show(charts[0]); break;
                            }
                            string status = $"stage={stage} renderer={overlay.RendererDescription} tiles={overlay.TileUpdates} commits={overlay.AnimationCommits}";
                            report.AppendLine(status);
                            Console.WriteLine(status);
                        }
                        if (steady || stage != 6) // a three second source stall
                            overlay.Sync(steady || stage < 5 ? elapsed.Elapsed.TotalMilliseconds + 10000
                                : elapsed.Elapsed.TotalMilliseconds - 15000);
                        if (seconds < (heartbeat || unpaced || noteVisual ? 90 : opaque ? 36 : 24))
                            return;
                        if (opaque || unpaced)
                        {
                            double[] intervals = overlay.FrameIntervals.Order().ToArray();
                            if (intervals.Length < 100) throw new InvalidOperationException("Opaque renderer did not produce frames");
                            string cadence = $"Application cadence: count={intervals.Length} p50={intervals[intervals.Length / 2]:0.00}ms p95={intervals[(int)(intervals.Length * 0.95)]:0.00}ms";
                            report.AppendLine(cadence);
                            Console.WriteLine(cadence);
                        }
                        if (!overlay.RendererDescription.StartsWith(noteVisual ? "DComp note visuals" : unpaced ? "legacy" : hwndAlpha ? "transparent HWND" : opaque ? "opaque" : "DComp retained"))
                            throw new InvalidOperationException("Retained renderer fell back to legacy rendering");
                        if (!opaque && !unpaced && (overlay.TileUpdates < 12 || (!steady && overlay.AnimationCommits < 100)))
                            throw new InvalidOperationException("Tile replenishment or animation maintenance did not run");
                        timer.Stop();
                        overlay.Hide();
                        owner.Close();
                        Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                        timer.Stop();
                        owner.Close();
                        Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    }
                };
                timer.Start();
                Dispatcher.Run();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        report.AppendLine(failure is null ? "PASS" : failure.ToString());
        if (reportPath is not null)
            System.IO.File.WriteAllText(reportPath, report.ToString());
        Console.WriteLine(failure is null ? "Preview graphics smoke test passed." : failure);
        return failure is null ? 0 : 1;
    }

    private static PreviewOverlay CreateHeartbeat(Window owner)
    {
        PreviewOverlay.OpaqueMode = PreviewOverlay.LegacyMode = true;
        PreviewOverlay beat = new();
        PreviewOverlay.OpaqueMode = PreviewOverlay.LegacyMode = false;
        beat.SetOwner(new WindowInteropHelper(owner).Handle);
        beat.SetBounds(1110, 150, 40, 40);
        beat.Show(BuildChart(4));
        return beat;
    }

    private static ManiaPreviewChart BuildChart(int keys)
    {
        StringBuilder osu = new($"osu file format v14\n[General]\nMode:3\n[Difficulty]\nCircleSize:{keys}\n[HitObjects]\n");
        for (int i = 0; i < 800; i++)
        {
            int time = i * 90;
            int x = (i * 3 % keys) * 512 / keys + 256 / keys;
            osu.AppendLine(i % 9 == 0
                ? $"{x},192,{time},128,0,{time + 2500}:0:0:0:0:"
                : $"{x},192,{time},1,0,0:0:0:0:");
        }
        return ManiaPreviewChart.Parse(osu.ToString())!;
    }
}
