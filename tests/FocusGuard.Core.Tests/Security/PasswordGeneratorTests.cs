using FocusGuard.Core.Security;
using Xunit;

namespace FocusGuard.Core.Tests.Security;

public class PasswordGeneratorTests
{
    private readonly PasswordGenerator _generator = new();

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(50)]
    [InlineData(100)]
    public void Generate_ReturnsCorrectLength(int length)
    {
        var password = _generator.Generate(length, PasswordDifficulty.Medium);
        Assert.Equal(length, password.Length);
    }

    [Fact]
    public void Generate_Easy_ContainsOnlyLowercaseLetters()
    {
        var password = _generator.Generate(100, PasswordDifficulty.Easy);
        Assert.Matches("^[a-z]+$", password);
    }

    [Fact]
    public void Generate_Medium_ContainsMixedCaseAndDigits()
    {
        // Generate a long password to statistically ensure all character groups appear
        var password = _generator.Generate(200, PasswordDifficulty.Medium);

        Assert.Contains(password, c => char.IsLower(c));
        Assert.Contains(password, c => char.IsUpper(c));
        Assert.Contains(password, c => char.IsDigit(c));
        // Should not contain special characters
        Assert.DoesNotContain(password, c => "!@#$%&*?".Contains(c));
    }

    [Fact]
    public void Generate_Hard_ContainsMixedCaseDigitsAndSpecials()
    {
        var password = _generator.Generate(200, PasswordDifficulty.Hard);

        Assert.Contains(password, c => char.IsLower(c));
        Assert.Contains(password, c => char.IsUpper(c));
        Assert.Contains(password, c => char.IsDigit(c));
        Assert.Contains(password, c => "!@#$%&*?".Contains(c));
    }

    [Fact]
    public void Generate_Medium_GuaranteesAtLeastOneFromEachGroup()
    {
        // Even with short passwords, each required group must be represented
        for (var i = 0; i < 50; i++)
        {
            var password = _generator.Generate(5, PasswordDifficulty.Medium);
            Assert.Equal(5, password.Length);
            Assert.Contains(password, c => char.IsLower(c));
            Assert.Contains(password, c => char.IsUpper(c));
            Assert.Contains(password, c => char.IsDigit(c));
        }
    }

    [Fact]
    public void Generate_Hard_GuaranteesAtLeastOneFromEachGroup()
    {
        for (var i = 0; i < 50; i++)
        {
            var password = _generator.Generate(6, PasswordDifficulty.Hard);
            Assert.Equal(6, password.Length);
            Assert.Contains(password, c => char.IsLower(c));
            Assert.Contains(password, c => char.IsUpper(c));
            Assert.Contains(password, c => char.IsDigit(c));
            Assert.Contains(password, c => "!@#$%&*?".Contains(c));
        }
    }

    [Fact]
    public void Generate_ProducesDifferentOutputs()
    {
        var passwords = new HashSet<string>();
        for (var i = 0; i < 100; i++)
        {
            passwords.Add(_generator.Generate(30, PasswordDifficulty.Medium));
        }

        // All 100 passwords should be unique (cryptographic randomness)
        Assert.Equal(100, passwords.Count);
    }

    [Fact]
    public void Generate_ZeroLength_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _generator.Generate(0, PasswordDifficulty.Easy));
    }

    [Fact]
    public void Generate_NegativeLength_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _generator.Generate(-1, PasswordDifficulty.Easy));
    }

    [Fact]
    public void Generate_LengthOne_Easy_ReturnsOneChar()
    {
        var password = _generator.Generate(1, PasswordDifficulty.Easy);
        Assert.Single(password);
        Assert.Matches("^[a-z]$", password);
    }

    [Theory]
    [InlineData(PasswordDifficulty.Easy)]
    [InlineData(PasswordDifficulty.Medium)]
    [InlineData(PasswordDifficulty.Hard)]
    public void Generate_AllDifficulties_ReturnNonEmpty(PasswordDifficulty difficulty)
    {
        var password = _generator.Generate(30, difficulty);
        Assert.NotEmpty(password);
    }
}
