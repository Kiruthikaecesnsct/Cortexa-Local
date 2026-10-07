using Collector.Application.Auth;

namespace Collector.Tests.Auth;

public class SignInRulesTests
{
    [Fact]
    public void Accepts_valid_input()
    {
        Assert.Null(SignInRules.Validate("  user@example.com ", "secret"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_empty_email(string? email)
    {
        var result = SignInRules.Validate(email, "secret");

        Assert.Equal(SignInField.Email, result?.Field);
    }

    [Fact]
    public void Rejects_email_without_at_sign()
    {
        Assert.Equal(SignInField.Email, SignInRules.Validate("user.example.com", "secret")?.Field);
    }

    [Fact]
    public void Enforces_email_length_limit()
    {
        var atLimit = new string('a', SignInRules.MaxEmailLength - 2) + "@b";
        var overLimit = atLimit + "c";

        Assert.Null(SignInRules.Validate(atLimit, "secret"));
        Assert.Equal(SignInField.Email, SignInRules.Validate(overLimit, "secret")?.Field);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Rejects_empty_password(string? password)
    {
        Assert.Equal(SignInField.Password, SignInRules.Validate("user@example.com", password)?.Field);
    }

    [Fact]
    public void Enforces_password_length_limit()
    {
        var atLimit = new string('p', SignInRules.MaxPasswordLength);

        Assert.Null(SignInRules.Validate("user@example.com", atLimit));
        Assert.Equal(SignInField.Password, SignInRules.Validate("user@example.com", atLimit + "p")?.Field);
    }
}
