namespace FocusGuard.App.Models;

public class HeatmapDay
{
    public DateTime Date { get; set; }
    public double FocusMinutes { get; set; }
    public int IntensityLevel { get; set; } // 0-4
    public string Color { get; set; } = "#2A2A3E";
    public string ToolTipText { get; set; } = string.Empty;

    public static int CalculateIntensity(double minutes)
    {
        return minutes switch
        {
            0 => 0,
            < 30 => 1,
            < 60 => 2,
            < 120 => 3,
            _ => 4
        };
    }

    public static string IntensityToColor(int level)
    {
        return level switch
        {
            0 => "#2A2A3E",   // Empty — surface color
            1 => "#0E4429",   // Light green
            2 => "#006D32",   // Medium green
            3 => "#26A641",   // Bright green
            4 => "#39D353",   // Intense green
            _ => "#2A2A3E"
        };
    }
}
