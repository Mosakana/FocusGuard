namespace FocusGuard.Core.Configuration;

public static class AppPaths
{
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FocusGuard");

    public static string DatabasePath =>
        Path.Combine(DataDirectory, "focusguard.db");

    public static string LogDirectory =>
        Path.Combine(DataDirectory, "logs");
}
