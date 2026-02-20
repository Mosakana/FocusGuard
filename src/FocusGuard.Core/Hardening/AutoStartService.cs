using FocusGuard.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace FocusGuard.Core.Hardening;

public class AutoStartService : IAutoStartService
{
    private const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FocusGuard";
    private readonly ILogger<AutoStartService> _logger;

    public AutoStartService(ILogger<AutoStartService> logger)
    {
        _logger = logger;
    }

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check auto-start registry key");
            return false;
        }
    }

    public void Enable()
    {
        if (AppPaths.IsPortableMode)
        {
            _logger.LogWarning("Auto-start is not supported in portable mode");
            return;
        }

        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                _logger.LogWarning("Cannot determine process path for auto-start");
                return;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
            key?.SetValue(ValueName, $"\"{exePath}\" --minimized");
            _logger.LogInformation("Auto-start enabled: {Path}", exePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable auto-start");
        }
    }

    public void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            _logger.LogInformation("Auto-start disabled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disable auto-start");
        }
    }
}
