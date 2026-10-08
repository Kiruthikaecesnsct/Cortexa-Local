using Collector.Application.Settings;

namespace Collector.Tests.Settings;

public sealed class RemoteSourceRulesTests
{
    private const int MaxOrganizationLength = 50;

    [Theory]
    [InlineData("org")]
    [InlineData("my-org1")]
    [InlineData("1org")]
    public void IsValidOrganization_ValidNames_ReturnsTrue(string name)
    {
        Assert.True(RemoteSourceRules.IsValidOrganization(name));
    }

    [Fact]
    public void IsValidOrganization_MaxLengthName_ReturnsTrue()
    {
        Assert.True(RemoteSourceRules.IsValidOrganization(new string('a', MaxOrganizationLength)));
    }

    [Fact]
    public void IsValidOrganization_OverMaxLengthName_ReturnsFalse()
    {
        Assert.False(RemoteSourceRules.IsValidOrganization(new string('a', MaxOrganizationLength + 1)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-org")]
    [InlineData("org\n")]
    [InlineData("org ")]
    [InlineData("a/b")]
    [InlineData("a_b")]
    public void IsValidOrganization_InvalidNames_ReturnsFalse(string? name)
    {
        Assert.False(RemoteSourceRules.IsValidOrganization(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("my-org")]
    public void Validate_BlankOrValidName_IsValid(string name)
    {
        Assert.True(RemoteSourceRules.Validate(new RemoteSourceSettings(name)).IsValid);
    }

    [Fact]
    public void Validate_NameWithSlash_ReturnsInvalidForOrganizationField()
    {
        var result = RemoteSourceRules.Validate(new RemoteSourceSettings("a/b"));

        Assert.Equal(EndpointField.AzureDevOpsOrganization, result.Field);
    }

    [Fact]
    public void Validate_NameWithInnerNewline_ReturnsInvalid()
    {
        var result = RemoteSourceRules.Validate(new RemoteSourceSettings("or\ng"));

        Assert.False(result.IsValid);
    }
}
