using Collector.Application.Remote;
using Collector.Domain.Enums;

namespace Collector.Tests.Remote;

public sealed class OrgUrlParserTests
{
    [Theory]
    [InlineData("https://github.com/octo", "octo")]
    [InlineData("https://github.com/orgs/octo", "octo")]
    [InlineData("https://www.github.com/octo", "octo")]
    [InlineData("https://github.com/octo/", "octo")]
    [InlineData("https://github.com/orgs/octo/", "octo")]
    [InlineData("  https://github.com/octo  ", "octo")]
    [InlineData("https://github.com/my-org-1", "my-org-1")]
    public void Parse_ValidGitHubUrl_ReturnsTheOrganization(string input, string expected)
    {
        var result = OrgUrlParser.Parse(SourceType.Github, input);

        Assert.Equal(new OrgUrl(SourceType.Github, expected), result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("http://github.com/octo")]
    [InlineData("github.com/octo")]
    [InlineData("octo")]
    [InlineData("https://github.com/octo/repo")]
    [InlineData("https://github.com/orgs/octo/repos")]
    [InlineData("https://github.com/-octo")]
    [InlineData("https://github.com/octo?tab=repositories")]
    [InlineData("https://gitlab.com/octo")]
    [InlineData("https://dev.azure.com/contoso")]
    public void Parse_InvalidGitHubUrl_ReturnsNull(string? input) =>
        Assert.Null(OrgUrlParser.Parse(SourceType.Github, input));

    [Fact]
    public void Parse_GitHubOrganizationAtTheLengthLimit_IsAccepted()
    {
        var organization = new string('a', 39);

        var result = OrgUrlParser.Parse(SourceType.Github, $"https://github.com/{organization}");

        Assert.Equal(organization, result!.Organization);
    }

    [Fact]
    public void Parse_GitHubOrganizationTooLong_ReturnsNull() =>
        Assert.Null(OrgUrlParser.Parse(SourceType.Github, $"https://github.com/{new string('a', 40)}"));

    [Theory]
    [InlineData("https://dev.azure.com/contoso", "contoso")]
    [InlineData("https://dev.azure.com/contoso/", "contoso")]
    [InlineData("  https://dev.azure.com/my-org  ", "my-org")]
    public void Parse_ValidAzureDevOpsUrl_ReturnsTheOrganization(string input, string expected)
    {
        var result = OrgUrlParser.Parse(SourceType.AzureDevops, input);

        Assert.Equal(new OrgUrl(SourceType.AzureDevops, expected), result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("contoso")]
    [InlineData("http://dev.azure.com/contoso")]
    [InlineData("https://dev.azure.com/")]
    [InlineData("https://dev.azure.com/contoso/Research")]
    [InlineData("https://dev.azure.com/contoso/Research/_git/repo")]
    [InlineData("https://contoso.visualstudio.com")]
    [InlineData("https://github.com/contoso")]
    public void Parse_InvalidAzureDevOpsUrl_ReturnsNull(string? input) =>
        Assert.Null(OrgUrlParser.Parse(SourceType.AzureDevops, input));

    [Fact]
    public void Parse_AzureDevOpsOrganizationTooLong_ReturnsNull() =>
        Assert.Null(OrgUrlParser.Parse(SourceType.AzureDevops, $"https://dev.azure.com/{new string('a', 51)}"));

    [Theory]
    [InlineData(SourceType.Local)]
    [InlineData(SourceType.Ssh)]
    [InlineData(SourceType.CortexaRepo)]
    public void Parse_ProviderWithoutOrganizations_ReturnsNull(SourceType provider) =>
        Assert.Null(OrgUrlParser.Parse(provider, "https://github.com/octo"));
}
