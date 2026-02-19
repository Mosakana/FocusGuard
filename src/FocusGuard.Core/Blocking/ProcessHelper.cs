using System.Diagnostics;

namespace FocusGuard.Core.Blocking;

public static class ProcessHelper
{
    public static string NormalizeProcessName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var normalized = name.Trim().ToLowerInvariant();

        // Remove .exe extension if present
        if (normalized.EndsWith(".exe", StringComparison.Ordinal))
            normalized = normalized[..^4];

        return normalized;
    }

    public static List<string> GetRunningProcessNames()
    {
        try
        {
            return Process.GetProcesses()
                .Select(p =>
                {
                    try { return p.ProcessName.ToLowerInvariant(); }
                    catch { return null; }
                    finally { p.Dispose(); }
                })
                .Where(n => n is not null)
                .Distinct()
                .ToList()!;
        }
        catch
        {
            return [];
        }
    }
}
