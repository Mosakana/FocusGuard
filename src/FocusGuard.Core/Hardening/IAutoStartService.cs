namespace FocusGuard.Core.Hardening;

public interface IAutoStartService
{
    bool IsEnabled();
    void Enable();
    void Disable();
}
