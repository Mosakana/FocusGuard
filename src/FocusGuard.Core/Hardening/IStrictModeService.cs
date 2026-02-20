namespace FocusGuard.Core.Hardening;

public interface IStrictModeService
{
    Task<bool> IsEnabledAsync();
    Task SetEnabledAsync(bool enabled);
    Task<bool> CanToggleAsync();
}
