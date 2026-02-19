using System.Security.Cryptography;
using System.Text;

namespace FocusGuard.Core.Security;

public class PasswordGenerator
{
    private const string LowercaseChars = "abcdefghijklmnopqrstuvwxyz";
    private const string UppercaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string DigitChars = "0123456789";
    private const string SpecialChars = "!@#$%&*?";

    /// <summary>
    /// Generates a cryptographically random password string.
    /// Uses System.Security.Cryptography.RandomNumberGenerator.
    /// </summary>
    /// <param name="length">Password length (minimum 1).</param>
    /// <param name="difficulty">Character set difficulty level.</param>
    /// <returns>A random password string of the specified length.</returns>
    public string Generate(int length, PasswordDifficulty difficulty)
    {
        if (length < 1)
            throw new ArgumentOutOfRangeException(nameof(length), "Password length must be at least 1.");

        var (charPool, requiredGroups) = GetCharacterSets(difficulty);

        // For very short passwords where we can't guarantee one from each group,
        // just pick randomly from the full pool
        if (length < requiredGroups.Count)
        {
            return GenerateFromPool(charPool, length);
        }

        // Ensure at least one character from each required group
        var password = new char[length];
        var usedPositions = new HashSet<int>();

        // Place one guaranteed character from each required group at random positions
        foreach (var group in requiredGroups)
        {
            int position;
            do
            {
                position = RandomNumberGenerator.GetInt32(length);
            } while (!usedPositions.Add(position));

            password[position] = group[RandomNumberGenerator.GetInt32(group.Length)];
        }

        // Fill remaining positions from the full character pool
        for (var i = 0; i < length; i++)
        {
            if (!usedPositions.Contains(i))
            {
                password[i] = charPool[RandomNumberGenerator.GetInt32(charPool.Length)];
            }
        }

        return new string(password);
    }

    private static (string charPool, List<string> requiredGroups) GetCharacterSets(PasswordDifficulty difficulty)
    {
        return difficulty switch
        {
            PasswordDifficulty.Easy => (
                LowercaseChars,
                new List<string> { LowercaseChars }
            ),
            PasswordDifficulty.Medium => (
                LowercaseChars + UppercaseChars + DigitChars,
                new List<string> { LowercaseChars, UppercaseChars, DigitChars }
            ),
            PasswordDifficulty.Hard => (
                LowercaseChars + UppercaseChars + DigitChars + SpecialChars,
                new List<string> { LowercaseChars, UppercaseChars, DigitChars, SpecialChars }
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(difficulty))
        };
    }

    private static string GenerateFromPool(string charPool, int length)
    {
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            sb.Append(charPool[RandomNumberGenerator.GetInt32(charPool.Length)]);
        }
        return sb.ToString();
    }
}
