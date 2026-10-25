using System.Windows;
using System.Windows.Media;

namespace OsuRoam.Controls;

public sealed class ProgressRing : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(ProgressRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(ProgressRing),
        new FrameworkPropertyMetadata(3d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(ProgressRing),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProgressBrushProperty = DependencyProperty.Register(
        nameof(ProgressBrush), typeof(Brush), typeof(ProgressRing),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, Math.Clamp(value, 0, 1));
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush ProgressBrush
    {
        get => (Brush)GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        double thickness = Math.Max(0, StrokeThickness);
        double radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - thickness / 2);
        if (radius <= 0 || thickness <= 0)
            return;

        Point center = new(ActualWidth / 2, ActualHeight / 2);
        Pen trackPen = new(TrackBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        drawingContext.DrawEllipse(null, trackPen, center, radius, radius);

        double progress = Math.Clamp(Progress, 0, 1);
        if (progress <= 0)
            return;
        if (progress >= 1)
        {
            Pen completePen = new(ProgressBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            drawingContext.DrawEllipse(null, completePen, center, radius, radius);
            return;
        }

        Point start = new(center.X, center.Y - radius);
        double angle = -Math.PI / 2 + progress * Math.PI * 2;
        Point end = new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, new Size(radius, radius), 0, progress > 0.5,
                SweepDirection.Clockwise, true, false);
        }

        Pen progressPen = new(ProgressBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        drawingContext.DrawGeometry(null, progressPen, geometry);
    }
}
