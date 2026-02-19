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
        catch (IOException ex)
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
        catch (IOException ex)
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
                sb.AppendLine($"127.0.0.1 {domain}");
            }
            sb.AppendLine(MarkerEnd);
        }

        sb.AppendLine();

        // Atomic write: write to temp file then replace
        var tempPath = HostsFilePath + ".focusguard.tmp";
        await File.WriteAllTextAsync(tempPath, sb.ToString());
        File.Move(tempPath, HostsFilePath, overwrite: true);
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
        try
        {
            var psi = new ProcessStartInfo("ipconfig", "/flushdns")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            using var process = Process.Start(psi);
            if (process is not null)
            {
                await process.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush DNS cache");
        }
    }
}
