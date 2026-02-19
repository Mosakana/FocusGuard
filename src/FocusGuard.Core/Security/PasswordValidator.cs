namespace FocusGuard.Core.Security;

public class PasswordValidator
{
    /// <summary>
    /// Validates that the user-typed input exactly matches the generated password.
    /// Uses ordinal comparison (case-sensitive, culture-invariant).
    /// </summary>
    public bool Validate(string expected, string actual)
    {
        return string.Equals(expected, actual, StringComparison.Ordinal);
    }
}
