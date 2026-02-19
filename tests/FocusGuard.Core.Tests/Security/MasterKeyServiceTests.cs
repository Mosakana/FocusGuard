using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Security;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FocusGuard.Core.Tests.Security;

public class MasterKeyServiceTests
{
    private readonly Mock<ISettingsRepository> _settingsMock;
    private readonly MasterKeyService _service;

    // In-memory settings store for the mock
    private readonly Dictionary<string, string> _store = new();

    public MasterKeyServiceTests()
    {
        _settingsMock = new Mock<ISettingsRepository>();

        _settingsMock.Setup(s => s.GetAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => _store.GetValueOrDefault(key));

        _settingsMock.Setup(s => s.SetAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((key, value) => _store[key] = value)
            .Returns(Task.CompletedTask);

        _settingsMock.Setup(s => s.ExistsAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => _store.ContainsKey(key));

        var logger = new Mock<ILogger<MasterKeyService>>();
        _service = new MasterKeyService(_settingsMock.Object, logger.Object);
    }

    [Fact]
    public async Task GenerateMasterKeyAsync_ReturnsNonEmptyKey()
    {
        var key = await _service.GenerateMasterKeyAsync();

        Assert.NotNull(key);
        Assert.NotEmpty(key);
        Assert.Equal(32, key.Length); // 16 bytes = 32 hex chars
    }

    [Fact]
    public async Task GenerateMasterKeyAsync_Returns32HexChars()
    {
        var key = await _service.GenerateMasterKeyAsync();

        Assert.Matches("^[0-9a-f]{32}$", key);
    }

    [Fact]
    public async Task GenerateMasterKeyAsync_StoresHashAndSalt()
    {
        await _service.GenerateMasterKeyAsync();

        Assert.True(_store.ContainsKey(SettingsKeys.MasterKeyHash));
        Assert.True(_store.ContainsKey(SettingsKeys.MasterKeySalt));
        Assert.NotEmpty(_store[SettingsKeys.MasterKeyHash]);
        Assert.NotEmpty(_store[SettingsKeys.MasterKeySalt]);
    }

    [Fact]
    public async Task GenerateMasterKeyAsync_StoredHashIsNotPlaintext()
    {
        var key = await _service.GenerateMasterKeyAsync();

        // The stored hash should NOT be the plaintext key
        Assert.NotEqual(key, _store[SettingsKeys.MasterKeyHash]);
    }

    [Fact]
    public async Task GenerateMasterKeyAsync_GeneratesUniqueKeysEachTime()
    {
        var key1 = await _service.GenerateMasterKeyAsync();

        // Reset store for a fresh generation
        _store.Clear();
        var key2 = await _service.GenerateMasterKeyAsync();

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public async Task ValidateMasterKeyAsync_CorrectKey_ReturnsTrue()
    {
        var key = await _service.GenerateMasterKeyAsync();

        var result = await _service.ValidateMasterKeyAsync(key);

        Assert.True(result);
    }

    [Fact]
    public async Task ValidateMasterKeyAsync_WrongKey_ReturnsFalse()
    {
        await _service.GenerateMasterKeyAsync();

        var result = await _service.ValidateMasterKeyAsync("wrongkeywrongkeywrongkeywrongkeyw");

        Assert.False(result);
    }

    [Fact]
    public async Task ValidateMasterKeyAsync_EmptyKey_ReturnsFalse()
    {
        await _service.GenerateMasterKeyAsync();

        Assert.False(await _service.ValidateMasterKeyAsync(""));
        Assert.False(await _service.ValidateMasterKeyAsync("   "));
    }

    [Fact]
    public async Task ValidateMasterKeyAsync_NoKeySetUp_ReturnsFalse()
    {
        // Don't generate any key
        var result = await _service.ValidateMasterKeyAsync("somekey");

        Assert.False(result);
    }

    [Fact]
    public async Task ValidateMasterKeyAsync_CaseInsensitiveHex_ReturnsTrue()
    {
        var key = await _service.GenerateMasterKeyAsync();

        // Hex keys should validate regardless of case
        var result = await _service.ValidateMasterKeyAsync(key.ToUpperInvariant());

        Assert.True(result);
    }

    [Fact]
    public async Task ValidateMasterKeyAsync_WithWhitespace_ReturnsTrue()
    {
        var key = await _service.GenerateMasterKeyAsync();

        // Should trim whitespace before validating
        var result = await _service.ValidateMasterKeyAsync("  " + key + "  ");

        Assert.True(result);
    }

    [Fact]
    public async Task IsSetupCompleteAsync_NoKeySet_ReturnsFalse()
    {
        var result = await _service.IsSetupCompleteAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task IsSetupCompleteAsync_AfterGenerate_ReturnsTrue()
    {
        await _service.GenerateMasterKeyAsync();

        var result = await _service.IsSetupCompleteAsync();

        Assert.True(result);
    }
}
