using FocusGuard.Core.Blocking;
using FocusGuard.Core.Data.Entities;
using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Statistics;

public class BlockedAttemptLogger
{
    private readonly IBlockedAttemptRepository _repository;
    private readonly IFocusSessionManager _sessionManager;
    private readonly IApplicationBlocker _applicationBlocker;
    private readonly ILogger<BlockedAttemptLogger> _logger;

    public event EventHandler<BlockedAttemptEntity>? AttemptLogged;

    public BlockedAttemptLogger(
        IBlockedAttemptRepository repository,
        IFocusSessionManager sessionManager,
        IApplicationBlocker applicationBlocker,
        ILogger<BlockedAttemptLogger> logger)
    {
        _repository = repository;
        _sessionManager = sessionManager;
        _applicationBlocker = applicationBlocker;
        _logger = logger;

        _applicationBlocker.ProcessBlocked += OnProcessBlocked;
    }

    private async void OnProcessBlocked(object? sender, BlockedProcessEventArgs e)
    {
        try
        {
            var attempt = new BlockedAttemptEntity
            {
                SessionId = _sessionManager.CurrentSession?.SessionId,
                Timestamp = e.Timestamp,
                Type = "Application",
                Target = e.ProcessName
            };

            await _repository.CreateAsync(attempt);
            AttemptLogged?.Invoke(this, attempt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log blocked attempt for {ProcessName}", e.ProcessName);
        }
    }
}
