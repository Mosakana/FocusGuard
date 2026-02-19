using System.Text.RegularExpressions;

namespace FocusGuard.Core.Blocking;

public static partial class DomainHelper
{
    [GeneratedRegex(@"^([a-zA-Z0-9]([a-zA-Z0-9\-]*[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}$")]
    private static partial Regex DomainRegex();

    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var domain = input.Trim().ToLowerInvariant();

        // Strip protocol
        if (domain.StartsWith("https://", StringComparison.Ordinal))
            domain = domain[8..];
        else if (domain.StartsWith("http://", StringComparison.Ordinal))
            domain = domain[7..];

        // Strip path, query, fragment
        var slashIndex = domain.IndexOf('/');
        if (slashIndex >= 0)
            domain = domain[..slashIndex];

        // Strip port
        var colonIndex = domain.IndexOf(':');
        if (colonIndex >= 0)
            domain = domain[..colonIndex];

        return domain;
    }

    public static List<string> Expand(string domain)
    {
        var normalized = Normalize(domain);
        if (string.IsNullOrEmpty(normalized))
            return [];

        var result = new List<string> { normalized };

        if (!normalized.StartsWith("www.", StringComparison.Ordinal))
        {
            result.Add("www." + normalized);
        }

        return result;
    }

    public static bool IsValid(string domain)
    {
        var normalized = Normalize(domain);
        return !string.IsNullOrEmpty(normalized) && DomainRegex().IsMatch(normalized);
    }
}
