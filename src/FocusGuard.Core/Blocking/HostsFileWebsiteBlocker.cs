using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Blocking;

public class HostsFileWebsiteBlocker : IWebsiteBlocker
{
    private const string HostsFilePath = @"C:\Windows\System32\drivers\etc\hosts";
    private const string MarkerStart = "# >>> FocusGuard START - DO NOT EDIT <<<";
    private const string MarkerEnd = "# >>> FocusGuard END <<<";

    private readonly ILogger<HostsFileWebsiteBlocker> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private List<string> _currentlyBlocked = [];

    public bool IsActive => _currentlyBlocked.Count > 0;

    public HostsFileWebsiteBlocker(ILogger<HostsFileWebsiteBlocker> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<string> GetCurrentlyBlocked() => _currentlyBlocked.AsReadOnly();

    public async Task ApplyBlocklistAsync(IEnumerable<string> domains)
    {
        await _semaphore.WaitAsync();
        try
        {
            var allDomains = domains
                .SelectMany(DomainHelper.Expand)
                .Where(d => DomainHelper.IsValid(d))
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            if (allDomains.Count == 0)
            {
                _logger.LogWarning("No valid domains to block");
                return;
            }

            await WriteHostsFileAsync(allDomains);
            _currentlyBlocked = allDomains;
            await FlushDnsAsync();

            _logger.LogInformation("Applied blocklist: {Count} domains", allDomains.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to modify hosts file. It may be locked by antivirus software.");
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task RemoveBlocklistAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            await WriteHostsFileAsync([]);
            _currentlyBlocked = [];
            await FlushDnsAsync();

            _logger.LogInformation("Removed blocklist from hosts file");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to restore hosts file");
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task WriteHostsFileAsync(List<string> domains)
    {
        string existingContent;
        try
        {
            existingContent = await File.ReadAllTextAsync(HostsFilePath);
        }
        catch (FileNotFoundException)
        {
            existingContent = string.Empty;
        }

        // Remove existing FocusGuard block
        var cleanContent = RemoveFocusGuardBlock(existingContent);

        var sb = new StringBuilder(cleanContent.TrimEnd());

        if (domains.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine(MarkerStart);
            foreach (var domain in domains)
            {
                sb.AppendLine($"0.0.0.0 {domain}");
            }
            sb.AppendLine(MarkerEnd);
        }

        sb.AppendLine();

        // Clean up stale temp file from previous failed attempt
        var tempPath = HostsFilePath + ".focusguard.tmp";
        try { File.Delete(tempPath); } catch { /* ignore */ }

        // Retry loop: hosts file can be briefly locked by DNS client or antivirus
        const int maxRetries = 5;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Clear readonly attribute if set
                var hostsFile = new FileInfo(HostsFilePath);
                if (hostsFile.Exists && hostsFile.IsReadOnly)
                {
                    hostsFile.IsReadOnly = false;
                }

                // Write directly to hosts file (avoid temp+move which is unreliable on system files)
                await File.WriteAllTextAsync(HostsFilePath, sb.ToString());
                return;
            }
            catch (Exception ex) when (attempt < maxRetries && (ex is IOException or UnauthorizedAccessException))
            {
                _logger.LogWarning("Hosts file write attempt {Attempt}/{Max} failed: {Message}. Retrying...",
                    attempt, maxRetries, ex.Message);
                await Task.Delay(200 * attempt);
            }
        }
    }

    private static string RemoveFocusGuardBlock(string content)
    {
        var lines = content.Split('\n');
        var result = new StringBuilder();
        var inBlock = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed == MarkerStart)
            {
                inBlock = true;
                continue;
            }
            if (trimmed == MarkerEnd)
            {
                inBlock = false;
                continue;
            }
            if (!inBlock)
            {
                result.AppendLine(trimmed);
            }
        }

        return result.ToString();
    }

    private async Task FlushDnsAsync()
    {
        // Flush Windows DNS resolver cache
        await RunCommandAsync("ipconfig", "/flushdns");

        // Also restart the DNS Client service for a more thorough flush
        // (net stop may fail on some Windows editions where dnscache is protected)
        await RunCommandAsync("net", "stop dnscache");
        await RunCommandAsync("net", "start dnscache");
    }

    private async Task RunCommandAsync(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            if (process is not null)
            {
                // Must read stdout/stderr to prevent buffer deadlock
                await process.StandardOutput.ReadToEndAsync();
                await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to run {Command}", $"{fileName} {arguments}");
        }
    }
}
