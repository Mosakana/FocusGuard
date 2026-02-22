namespace FocusGuard.App.Services;

public interface ITrayIconService : IDisposable
{
    void Initialize();
    void ShowBalloonTip(string title, string text, System.Windows.Forms.ToolTipIcon icon, int timeoutMs = 3000);
    void UpdateTooltip(string text);
}
