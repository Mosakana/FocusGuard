using System.Windows;
using System.Windows.Media;

namespace FocusGuard.App.Controls;

public class WeeklyMiniBarChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(double[]), typeof(WeeklyMiniBarChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarColorProperty =
        DependencyProperty.Register(nameof(BarColor), typeof(Brush), typeof(WeeklyMiniBarChart),
            new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public double[]? Values
    {
        get => (double[]?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush BarColor
    {
        get => (Brush)GetValue(BarColorProperty);
        set => SetValue(BarColorProperty, value);
    }

    private static readonly string[] DayLabels = ["M", "T", "W", "T", "F", "S", "S"];

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var width = ActualWidth;
        var height = ActualHeight;

        if (width <= 0 || height <= 0) return;

        var values = Values ?? new double[7];
        if (values.Length < 7) return;

        var labelHeight = 16.0;
        var chartHeight = height - labelHeight - 4;
        if (chartHeight <= 0) return;

        var maxVal = values.Max();
        if (maxVal <= 0) maxVal = 1;

        var barWidth = (width - 6 * 4) / 7; // 4px gap between bars
        var barColor = BarColor;
        var trackBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x50));
        trackBrush.Freeze();
        var textBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x90, 0xA0));
        textBrush.Freeze();
        var typeface = new Typeface("Segoe UI");

        for (int i = 0; i < 7; i++)
        {
            var x = i * (barWidth + 4);
            var barHeight = chartHeight * (values[i] / maxVal);
            var barY = chartHeight - barHeight;

            // Track (background bar)
            dc.DrawRoundedRectangle(trackBrush, null,
                new Rect(x, 0, barWidth, chartHeight), 2, 2);

            // Value bar
            if (values[i] > 0)
            {
                dc.DrawRoundedRectangle(barColor, null,
                    new Rect(x, barY, barWidth, barHeight), 2, 2);
            }

            // Day label
            var ft = new FormattedText(
                DayLabels[i], System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 10, textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            var labelX = x + (barWidth - ft.Width) / 2;
            dc.DrawText(ft, new Point(labelX, chartHeight + 4));
        }
    }
}
