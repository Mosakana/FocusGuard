using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Hardening;

public class WatchdogLauncher : IWatchdogLauncher
{
    private readonly ILogger<WatchdogLauncher> _logger;
    private Process? _watchdogProcess;

    public WatchdogLauncher(ILogger<WatchdogLauncher> logger)
    {
        _logger = logger;
    }

    public bool IsWatchdogRunning =>
        _watchdogProcess is not null && !_watchdogProcess.HasExited;

    public void Launch()
    {
        if (IsWatchdogRunning)
        {
            _logger.LogDebug("Watchdog already running");
            return;
        }

        var watchdogPath = Path.Combine(AppContext.BaseDirectory, "FocusGuard.Watchdog.exe");
        if (!File.Exists(watchdogPath))
        {
            _logger.LogWarning("Watchdog executable not found at {Path}", watchdogPath);
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = watchdogPath,
                UseShellExecute = true,
                Verb = "runas"
            };
            _watchdogProcess = Process.Start(psi);
            _logger.LogInformation("Watchdog launched (PID={Pid})", _watchdogProcess?.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch watchdog process");
        }
    }

    public void SignalStop()
    {
        try
        {
            if (_watchdogProcess is not null && !_watchdogProcess.HasExited)
            {
                _watchdogProcess.Kill();
                _logger.LogInformation("Watchdog process stopped");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to stop watchdog process");
        }
        finally
        {
            _watchdogProcess?.Dispose();
            _watchdogProcess = null;
        }
    }
}
