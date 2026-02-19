namespace FocusGuard.Core.Security;

public static class SettingsKeys
{
    public const string MasterKeyHash = "security.master_key_hash";
    public const string MasterKeySalt = "security.master_key_salt";
    public const string PasswordDifficulty = "session.password_difficulty";
    public const string PasswordLength = "session.password_length";
    public const string DefaultSessionDuration = "session.default_duration_minutes";
    public const string PomodoroWorkMinutes = "pomodoro.work_minutes";
    public const string PomodoroShortBreakMinutes = "pomodoro.short_break_minutes";
    public const string PomodoroLongBreakMinutes = "pomodoro.long_break_minutes";
    public const string PomodoroLongBreakInterval = "pomodoro.long_break_interval";
    public const string PomodoroAutoStart = "pomodoro.auto_start";
    public const string SoundEnabled = "notifications.sound_enabled";
    public const string MinimizeToTray = "app.minimize_to_tray";
}
