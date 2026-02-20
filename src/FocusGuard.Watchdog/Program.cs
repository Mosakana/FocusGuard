using System.Diagnostics;
using System.Text.Json;

namespace FocusGuard.Watchdog;

/// <summary>
/// Heartbeat data — duplicated from Core to avoid pulling in EF Core/Serilog dependencies.
/// </summary>
internal class HeartbeatData
{
    public int ProcessId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public bool HasActiveSession { get; set; }
    public Guid? ActiveSessionId { get; set; }
    public Guid? ActiveProfileId { get; set; }
    public string MainAppPath { get; set; } = string.Empty;
}

internal static class Program
{
    private const int PollIntervalMs = 5000;      // 5 seconds
    private const int StaleThresholdSeconds = 15;  // Heartbeat stale after 15s
    private const int MaxRestartAttempts = 3;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FocusGuard", "logs", "watchdog.log");

    private static readonly string HeartbeatPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FocusGuard", "heartbeat.json");

    static async Task Main()
    {
        Log("Watchdog started");

        var restartCount = 0;

        while (true)
        {
            await Task.Delay(PollIntervalMs);

            var heartbeat = await ReadHeartbeatAsync();

            // No heartbeat file → main app exited normally
            if (heartbeat is null)
            {
                Log("No heartbeat file found — main app exited normally. Watchdog exiting.");
                return;
            }

            // Main app is alive and has no active session → nothing to protect
            if (!heartbeat.HasActiveSession && IsProcessRunning(heartbeat.ProcessId))
            {
                Log("Main app running with no active session. Watchdog exiting.");
                return;
            }

            // Main app is alive — heartbeat is fresh
            if (IsProcessRunning(heartbeat.ProcessId))
            {
                restartCount = 0; // Reset counter while app is healthy
                continue;
            }

            // Main app is NOT running — check if heartbeat is stale
            var staleness = (DateTime.UtcNow - heartbeat.TimestampUtc).TotalSeconds;

            if (staleness > StaleThresholdSeconds && heartbeat.HasActiveSession)
            {
                restartCount++;
                Log($"Main app crashed with active session (stale {staleness:F0}s). Restart attempt {restartCount}/{MaxRestartAttempts}");

                if (restartCount > MaxRestartAttempts)
                {
                    Log("Max restart attempts exceeded. Watchdog giving up.");
                    return;
                }

                RestartMainApp(heartbeat.MainAppPath);
            }
            else if (!heartbeat.HasActiveSession)
            {
                Log("Main app exited without active session. Watchdog exiting.");
                return;
            }
        }
    }

    private static async Task<HeartbeatData?> ReadHeartbeatAsync()
    {
        try
        {
            if (!File.Exists(HeartbeatPath))
                return null;

            var json = await File.ReadAllTextAsync(HeartbeatPath);
            return JsonSerializer.Deserialize<HeartbeatData>(json);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static void RestartMainApp(string appPath)
    {
        try
        {
            if (string.IsNullOrEmpty(appPath) || !File.Exists(appPath))
            {
                Log($"Cannot restart — main app path invalid: {appPath}");
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = appPath,
                Arguments = "--recovered",
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(psi);
            Log($"Main app restarted: {appPath} --recovered");
        }
        catch (Exception ex)
        {
            Log($"Failed to restart main app: {ex.Message}");
        }
    }

    private static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (dir is not null)
                Directory.CreateDirectory(dir);

            var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} [Watchdog] {message}{Environment.NewLine}";
            File.AppendAllText(LogPath, line);
        }
        catch
        {
            // Best effort logging
        }
    }
}
