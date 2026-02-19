namespace FocusGuard.Core.Security;

public enum PasswordDifficulty
{
    Easy,   // Lowercase letters only: abcdefghij
    Medium, // Mixed case + digits: aB3kF9mQ2x
    Hard    // Mixed case + digits + specials: aB3$kF9@mQ
}
