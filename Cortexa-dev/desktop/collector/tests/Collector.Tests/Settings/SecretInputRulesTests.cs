using Collector.Application.Settings;

namespace Collector.Tests.Settings;

public class SecretInputRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankValue_AsksForValue(string? raw)
    {
        Assert.Equal("Enter a value.", SecretInputRules.Validate(raw));
    }

    [Fact]
    public void Validate_ValueOverMaxLength_ReportsLimit()
    {
        var raw = new string('x', SecretInputRules.MaxLength + 1);

        var reason = SecretInputRules.Validate(raw);

        Assert.Contains(SecretInputRules.MaxLength.ToString(), reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("two words")]
    [InlineData("new\nline")]
    public void Validate_InnerWhitespace_IsRejected(string raw)
    {
        Assert.Equal("The value must not contain spaces.", SecretInputRules.Validate(raw));
    }

    [Fact]
    public void Validate_PaddedToken_IsAcceptedAfterTrimming()
    {
        Assert.Null(SecretInputRules.Validate("  token  "));
    }

    [Fact]
    public void Validate_PaddingDoesNotCountTowardLength()
    {
        var raw = $"  {new string('x', SecretInputRules.MaxLength)}  ";

        Assert.Null(SecretInputRules.Validate(raw));
    }

    [Fact]
    public void Normalize_PaddedToken_ReturnsTrimmedToken()
    {
        Assert.Equal("token", SecretInputRules.Normalize("  token\r\n"));
    }

    [Fact]
    public void Normalize_InvalidValue_ThrowsWithReason()
    {
        var exception = Assert.Throws<ArgumentException>(() => SecretInputRules.Normalize("a b"));

        Assert.StartsWith("The value must not contain spaces.", exception.Message, StringComparison.Ordinal);
    }
}
