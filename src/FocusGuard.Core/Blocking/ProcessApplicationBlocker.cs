using System.Diagnostics;
using System.Management;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Blocking;

public class ProcessApplicationBlocker : IApplicationBlocker, IDisposable
{
    private readonly ILogger<ProcessApplicationBlocker> _logger;
    private ManagementEventWatcher? _watcher;
    private Timer? _pollingTimer;
    private HashSet<string> _blockedProcessNames = [];
    private bool _disposed;

    public bool IsActive => _blockedProcessNames.Count > 0;
    public event EventHandler<BlockedProcessEventArgs>? ProcessBlocked;

    public ProcessApplicationBlocker(ILogger<ProcessApplicationBlocker> logger)
    {
        _logger = logger;
    }

    public void StartBlocking(IEnumerable<string> processNames)
    {
        var names = processNames
            .Select(ProcessHelper.NormalizeProcessName)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet();

        if (names.Count == 0)
        {
            _logger.LogWarning("No valid process names to block");
            return;
        }

        _blockedProcessNames = names;

        // Kill any currently running blocked processes
        KillBlockedProcesses();

        // Start WMI watcher for real-time detection
        StartWmiWatcher();

        // Start polling timer as fallback
        _pollingTimer = new Timer(_ => KillBlockedProcesses(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        _logger.LogInformation("Started blocking {Count} processes: {Names}",
            names.Count, string.Join(", ", names));
    }

    public void StopBlocking()
    {
        StopWmiWatcher();

        _pollingTimer?.Dispose();
        _pollingTimer = null;

        var count = _blockedProcessNames.Count;
        _blockedProcessNames = [];

        _logger.LogInformation("Stopped blocking {Count} processes", count);
    }

    private void StartWmiWatcher()
    {
        try
        {
            var query = new WqlEventQuery(
                "__InstanceCreationEvent",
                TimeSpan.FromSeconds(1),
                "TargetInstance ISA 'Win32_Process'");

            _watcher = new ManagementEventWatcher(query);
            _watcher.EventArrived += OnProcessCreated;
            _watcher.Start();

            _logger.LogDebug("WMI process watcher started");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start WMI watcher, relying on polling only");
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    private void StopWmiWatcher()
    {
        if (_watcher is not null)
        {
            try
            {
                _watcher.EventArrived -= OnProcessCreated;
                _watcher.Stop();
                _watcher.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping WMI watcher");
            }
            _watcher = null;
        }
    }

    private void OnProcessCreated(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var processName = targetInstance["Name"]?.ToString();

            if (processName is null) return;

            var normalized = ProcessHelper.NormalizeProcessName(processName);
            if (_blockedProcessNames.Contains(normalized))
            {
                var processId = Convert.ToInt32(targetInstance["ProcessId"]);
                KillProcess(processId, normalized);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error handling WMI process creation event");
        }
    }

    private void KillBlockedProcesses()
    {
        if (_blockedProcessNames.Count == 0) return;

        foreach (var name in _blockedProcessNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                foreach (var process in processes)
                {
                    try
                    {
                        KillProcess(process.Id, name);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error polling for process: {Name}", name);
            }
        }
    }

    private void KillProcess(int processId, string processName)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);

            _logger.LogInformation("Killed blocked process: {Name} (PID: {Pid})", processName, processId);
            ProcessBlocked?.Invoke(this, new BlockedProcessEventArgs(processName));
        }
        catch (ArgumentException)
        {
            // Process already exited
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogWarning(ex, "Cannot kill protected process: {Name} (PID: {Pid})", processName, processId);
        }
        catch (InvalidOperationException)
        {
            // Process already exited
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopBlocking();
        GC.SuppressFinalize(this);
    }
}
