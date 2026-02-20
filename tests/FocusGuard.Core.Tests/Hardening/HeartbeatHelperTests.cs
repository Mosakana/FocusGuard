using FocusGuard.Core.Hardening;
using Xunit;

namespace FocusGuard.Core.Tests.Hardening;

public class HeartbeatHelperTests : IDisposable
{
    private readonly string _heartbeatPath;

    public HeartbeatHelperTests()
    {
        _heartbeatPath = HeartbeatHelper.HeartbeatFilePath;
        // Ensure clean state
        HeartbeatHelper.Delete();
    }

    public void Dispose()
    {
        HeartbeatHelper.Delete();
    }

    [Fact]
    public async Task WriteAndRead_RoundTrip()
    {
        var data = new HeartbeatData
        {
            ProcessId = 12345,
            TimestampUtc = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            HasActiveSession = true,
            ActiveSessionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ActiveProfileId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            MainAppPath = @"C:\Test\FocusGuard.App.exe"
        };

        await HeartbeatHelper.WriteAsync(data);
        var result = await HeartbeatHelper.ReadAsync();

        Assert.NotNull(result);
        Assert.Equal(data.ProcessId, result.ProcessId);
        Assert.Equal(data.TimestampUtc, result.TimestampUtc);
        Assert.Equal(data.HasActiveSession, result.HasActiveSession);
        Assert.Equal(data.ActiveSessionId, result.ActiveSessionId);
        Assert.Equal(data.ActiveProfileId, result.ActiveProfileId);
        Assert.Equal(data.MainAppPath, result.MainAppPath);
    }

    [Fact]
    public async Task ReadAsync_WhenMissing_ReturnsNull()
    {
        HeartbeatHelper.Delete();

        var result = await HeartbeatHelper.ReadAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task Delete_RemovesFile()
    {
        await HeartbeatHelper.WriteAsync(new HeartbeatData
        {
            ProcessId = 1,
            TimestampUtc = DateTime.UtcNow
        });

        HeartbeatHelper.Delete();

        Assert.False(File.Exists(_heartbeatPath));
    }

    [Fact]
    public void Delete_WhenMissing_DoesNotThrow()
    {
        HeartbeatHelper.Delete(); // Ensure not present
        HeartbeatHelper.Delete(); // Should not throw
    }
}
