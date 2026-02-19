using FocusGuard.Core.Security;
using Xunit;

namespace FocusGuard.Core.Tests.Security;

public class PasswordValidatorTests
{
    private readonly PasswordValidator _validator = new();

    [Fact]
    public void Validate_ExactMatch_ReturnsTrue()
    {
        Assert.True(_validator.Validate("aB3kF9mQ2x", "aB3kF9mQ2x"));
    }

    [Fact]
    public void Validate_CaseDifference_ReturnsFalse()
    {
        Assert.False(_validator.Validate("aB3kF9mQ2x", "ab3kf9mq2x"));
    }

    [Fact]
    public void Validate_WrongInput_ReturnsFalse()
    {
        Assert.False(_validator.Validate("correctPassword", "wrongPassword"));
    }

    [Fact]
    public void Validate_EmptyExpected_EmptyActual_ReturnsTrue()
    {
        Assert.True(_validator.Validate("", ""));
    }

    [Fact]
    public void Validate_EmptyExpected_NonEmptyActual_ReturnsFalse()
    {
        Assert.False(_validator.Validate("", "something"));
    }

    [Fact]
    public void Validate_NonEmptyExpected_EmptyActual_ReturnsFalse()
    {
        Assert.False(_validator.Validate("something", ""));
    }

    [Fact]
    public void Validate_WithSpecialChars_ExactMatch_ReturnsTrue()
    {
        Assert.True(_validator.Validate("aB3$kF9@mQ", "aB3$kF9@mQ"));
    }

    [Fact]
    public void Validate_WithSpecialChars_Mismatch_ReturnsFalse()
    {
        Assert.False(_validator.Validate("aB3$kF9@mQ", "aB3$kF9#mQ"));
    }

    [Fact]
    public void Validate_ExtraWhitespace_ReturnsFalse()
    {
        Assert.False(_validator.Validate("password", "password "));
        Assert.False(_validator.Validate("password", " password"));
    }

    [Fact]
    public void Validate_PartialMatch_ReturnsFalse()
    {
        Assert.False(_validator.Validate("longpassword123", "longpassword12"));
    }

    [Fact]
    public void Validate_NullExpected_NullActual_ReturnsTrue()
    {
        Assert.True(_validator.Validate(null!, null!));
    }

    [Fact]
    public void Validate_NullExpected_NonNullActual_ReturnsFalse()
    {
        Assert.False(_validator.Validate(null!, "something"));
    }
}
