using System.Security.Cryptography;
using FocusGuard.Core.Data.Repositories;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Security;

public class MasterKeyService
{
    private readonly ISettingsRepository _settings;
    private readonly ILogger<MasterKeyService> _logger;

    public MasterKeyService(ISettingsRepository settings, ILogger<MasterKeyService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Generates a new master recovery key (32 hex chars), hashes it (SHA-256 + salt),
    /// stores the hash in Settings. Returns the plaintext key (shown once to user).
    /// </summary>
    public async Task<string> GenerateMasterKeyAsync()
    {
        // Generate a 16-byte (32 hex char) random key
        var keyBytes = RandomNumberGenerator.GetBytes(16);
        var plaintextKey = Convert.ToHexString(keyBytes).ToLowerInvariant();

        // Generate a 16-byte random salt
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var saltHex = Convert.ToHexString(saltBytes).ToLowerInvariant();

        // Hash the key with salt: SHA-256(salt + key)
        var hashHex = ComputeHash(plaintextKey, saltBytes);

        // Store hash and salt
        await _settings.SetAsync(SettingsKeys.MasterKeySalt, saltHex);
        await _settings.SetAsync(SettingsKeys.MasterKeyHash, hashHex);

        _logger.LogInformation("Master recovery key generated and stored");

        return plaintextKey;
    }

    /// <summary>
    /// Validates a user-provided master key against the stored hash.
    /// </summary>
    public async Task<bool> ValidateMasterKeyAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var storedHash = await _settings.GetAsync(SettingsKeys.MasterKeyHash);
        var storedSalt = await _settings.GetAsync(SettingsKeys.MasterKeySalt);

        if (storedHash is null || storedSalt is null)
        {
            _logger.LogWarning("Master key validation attempted but no key is set up");
            return false;
        }

        // Reconstruct salt bytes from hex
        var saltBytes = Convert.FromHexString(storedSalt);

        // Hash the provided key with the stored salt
        var computedHash = ComputeHash(key.Trim().ToLowerInvariant(), saltBytes);

        var isValid = string.Equals(computedHash, storedHash, StringComparison.OrdinalIgnoreCase);

        if (isValid)
            _logger.LogWarning("Master key used for emergency unlock");
        else
            _logger.LogWarning("Invalid master key attempt");

        return isValid;
    }

    /// <summary>
    /// Returns true if a master key has been set up.
    /// </summary>
    public async Task<bool> IsSetupCompleteAsync()
    {
        return await _settings.ExistsAsync(SettingsKeys.MasterKeyHash);
    }

    private static string ComputeHash(string key, byte[] salt)
    {
        // Combine salt + key bytes
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
        var combined = new byte[salt.Length + keyBytes.Length];
        Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
        Buffer.BlockCopy(keyBytes, 0, combined, salt.Length, keyBytes.Length);

        var hashBytes = SHA256.HashData(combined);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
