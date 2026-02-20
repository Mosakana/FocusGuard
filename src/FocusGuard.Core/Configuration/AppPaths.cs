namespace FocusGuard.Core.Configuration;

public static class AppPaths
{
    private static bool? _isPortable;

    public static bool IsPortableMode =>
        _isPortable ??= File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.marker"));

    public static string DataDirectory =>
        IsPortableMode
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FocusGuard");

    public static string DatabasePath =>
        Path.Combine(DataDirectory, "focusguard.db");

    public static string LogDirectory =>
        Path.Combine(DataDirectory, "logs");
}
