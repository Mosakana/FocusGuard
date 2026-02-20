using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Hardening;

public class HeartbeatService : IHeartbeatService, IDisposable
{
    private readonly ILogger<HeartbeatService> _logger;
    private Timer? _timer;
    private Guid? _sessionId;
    private Guid? _profileId;

    public HeartbeatService(ILogger<HeartbeatService> logger)
    {
        _logger = logger;
    }

    public void Start(Guid? sessionId, Guid? profileId)
    {
        _sessionId = sessionId;
        _profileId = profileId;

        // Write immediately, then every 5 seconds
        WriteHeartbeat();
        _timer = new Timer(_ => WriteHeartbeat(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        _logger.LogInformation("Heartbeat started for session {SessionId}", sessionId);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        HeartbeatHelper.Delete();
        _sessionId = null;
        _profileId = null;
        _logger.LogInformation("Heartbeat stopped");
    }

    public void UpdateSession(Guid? sessionId, Guid? profileId)
    {
        _sessionId = sessionId;
        _profileId = profileId;
    }

    private void WriteHeartbeat()
    {
        try
        {
            var data = new HeartbeatData
            {
                ProcessId = Environment.ProcessId,
                TimestampUtc = DateTime.UtcNow,
                HasActiveSession = _sessionId.HasValue,
                ActiveSessionId = _sessionId,
                ActiveProfileId = _profileId,
                MainAppPath = Environment.ProcessPath ?? string.Empty
            };
            HeartbeatHelper.WriteAsync(data).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write heartbeat");
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
