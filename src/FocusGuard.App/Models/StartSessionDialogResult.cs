using FocusGuard.Core.Security;

namespace FocusGuard.App.Models;

public class StartSessionDialogResult
{
    public int DurationMinutes { get; init; }
    public bool EnablePomodoro { get; init; }
    public PasswordDifficulty Difficulty { get; init; }
}
