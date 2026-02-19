using FocusGuard.Core.Blocking;
using Xunit;

namespace FocusGuard.Core.Tests.Blocking;

public class DomainHelperTests
{
    [Theory]
    [InlineData("youtube.com", "youtube.com")]
    [InlineData("YOUTUBE.COM", "youtube.com")]
    [InlineData("https://youtube.com", "youtube.com")]
    [InlineData("http://youtube.com", "youtube.com")]
    [InlineData("https://youtube.com/watch?v=123", "youtube.com")]
    [InlineData("youtube.com:443", "youtube.com")]
    [InlineData("  youtube.com  ", "youtube.com")]
    [InlineData("https://www.YouTube.COM/path", "www.youtube.com")]
    public void Normalize_ReturnsExpectedDomain(string input, string expected)
    {
        Assert.Equal(expected, DomainHelper.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Normalize_EmptyOrNull_ReturnsEmpty(string? input)
    {
        Assert.Equal(string.Empty, DomainHelper.Normalize(input!));
    }

    [Fact]
    public void Expand_AddWwwPrefix()
    {
        var result = DomainHelper.Expand("youtube.com");
        Assert.Contains("youtube.com", result);
        Assert.Contains("www.youtube.com", result);
    }

    [Fact]
    public void Expand_AlreadyHasWww_DoesNotDuplicate()
    {
        var result = DomainHelper.Expand("www.youtube.com");
        Assert.Single(result);
        Assert.Contains("www.youtube.com", result);
    }

    [Theory]
    [InlineData("youtube.com", true)]
    [InlineData("www.youtube.com", true)]
    [InlineData("sub.domain.youtube.com", true)]
    [InlineData("a.co", true)]
    [InlineData("not valid", false)]
    [InlineData("", false)]
    [InlineData(".com", false)]
    [InlineData("com.", false)]
    public void IsValid_ReturnsExpected(string input, bool expected)
    {
        Assert.Equal(expected, DomainHelper.IsValid(input));
    }
}
