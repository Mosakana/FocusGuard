using System.Windows;
using System.Windows.Media;

namespace FocusGuard.App.Controls;

public class CircularProgressRing : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(CircularProgressRing),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(CircularProgressRing),
            new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProgressColorProperty =
        DependencyProperty.Register(nameof(ProgressColor), typeof(Brush), typeof(CircularProgressRing),
            new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackColorProperty =
        DependencyProperty.Register(nameof(TrackColor), typeof(Brush), typeof(CircularProgressRing),
            new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Progress value from 0.0 to 1.0.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>Thickness of the progress and track strokes.</summary>
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>Color of the progress arc.</summary>
    public Brush ProgressColor
    {
        get => (Brush)GetValue(ProgressColorProperty);
        set => SetValue(ProgressColorProperty, value);
    }

    /// <summary>Color of the background track circle.</summary>
    public Brush TrackColor
    {
        get => (Brush)GetValue(TrackColorProperty);
        set => SetValue(TrackColorProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size - StrokeThickness) / 2;

        if (radius <= 0) return;

        var trackPen = new Pen(TrackColor, StrokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        var progressPen = new Pen(ProgressColor, StrokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        // Draw track (full circle)
        dc.DrawEllipse(null, trackPen, center, radius, radius);

        // Draw progress arc
        var progress = Math.Clamp(Progress, 0.0, 1.0);
        if (progress <= 0) return;

        if (progress >= 0.999)
        {
            // Full circle — draw as ellipse to avoid rendering artifacts
            dc.DrawEllipse(null, progressPen, center, radius, radius);
            return;
        }

        var angle = progress * 360.0;
        var angleRad = angle * Math.PI / 180.0;

        // Start at 12 o'clock (top center)
        var startPoint = new Point(center.X, center.Y - radius);

        // End point: clockwise from 12 o'clock
        var endX = center.X + radius * Math.Sin(angleRad);
        var endY = center.Y - radius * Math.Cos(angleRad);
        var endPoint = new Point(endX, endY);

        var isLargeArc = angle > 180.0;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(startPoint, false, false);
            ctx.ArcTo(endPoint, new Size(radius, radius), 0,
                isLargeArc, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();

        dc.DrawGeometry(null, progressPen, geometry);
    }
}
