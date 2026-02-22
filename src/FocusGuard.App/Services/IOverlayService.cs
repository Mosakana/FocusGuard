namespace FocusGuard.App.Services;

public interface IOverlayService : IDisposable
{
    void Initialize();
    void ShowOverlay();
    void HideOverlay();
    bool IsVisible { get; }
}
