namespace FocusGuard.Core.Sessions;

public interface IFocusSessionManager
{
    FocusSessionState CurrentState { get; }
    FocusSessionInfo? CurrentSession { get; }

    Task StartSessionAsync(Guid profileId, int durationMinutes, bool pomodoroEnabled = false);
    Task ResumeSessionAsync(Guid sessionId, int remainingMinutes);
    Task<bool> TryUnlockAsync(string password);
    Task<bool> EmergencyUnlockAsync(string masterKey);
    Task EndSessionNaturallyAsync();
    string? GetUnlockPassword();
    void AdvancePomodoroInterval();

    event EventHandler<FocusSessionState>? StateChanged;
    event EventHandler? SessionEnded;
    event EventHandler<FocusSessionState>? PomodoroIntervalChanged;
}
