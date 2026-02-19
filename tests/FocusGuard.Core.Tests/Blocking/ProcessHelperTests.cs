using FocusGuard.Core.Blocking;
using Xunit;

namespace FocusGuard.Core.Tests.Blocking;

public class ProcessHelperTests
{
    [Theory]
    [InlineData("notepad.exe", "notepad")]
    [InlineData("NOTEPAD.EXE", "notepad")]
    [InlineData("notepad", "notepad")]
    [InlineData("  steam.exe  ", "steam")]
    [InlineData("Discord.exe", "discord")]
    public void NormalizeProcessName_ReturnsExpected(string input, string expected)
    {
        Assert.Equal(expected, ProcessHelper.NormalizeProcessName(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NormalizeProcessName_EmptyOrNull_ReturnsEmpty(string? input)
    {
        Assert.Equal(string.Empty, ProcessHelper.NormalizeProcessName(input!));
    }

    [Fact]
    public void GetRunningProcessNames_ReturnsNonEmptyList()
    {
        var processes = ProcessHelper.GetRunningProcessNames();
        Assert.NotEmpty(processes);
        // Should contain common system processes
        Assert.All(processes, p => Assert.NotNull(p));
    }
}
