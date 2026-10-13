using Collector.Application.Remote;
using Collector.Application.Remote.Auth;
using Collector.Domain.Enums;

namespace Collector.Tests.Remote;

public sealed class RemoteConnectRulesTests
{
    [Fact]
    public void OrganizationUrlError_GitHubInvalid_UsesTheWebExample() =>
        Assert.Equal(
            "Enter a URL like https://github.com/your-organization",
            RemoteConnectRules.OrganizationUrlError(SourceType.Github, "nope"));

    [Fact]
    public void OrganizationUrlError_AzureDevOpsInvalid_UsesTheWebExample() =>
        Assert.Equal(
            "Enter a URL like https://dev.azure.com/your-organization",
            RemoteConnectRules.OrganizationUrlError(SourceType.AzureDevops, "https://github.com/x"));

    [Fact]
    public void OrganizationUrlError_Valid_ReturnsNull() =>
        Assert.Null(RemoteConnectRules.OrganizationUrlError(SourceType.Github, "https://github.com/octo"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TokenError_Blank_AsksForTheToken(string? token) =>
        Assert.Equal("Enter a personal access token", RemoteConnectRules.TokenError(token));

    [Fact]
    public void TokenError_Present_ReturnsNull() => Assert.Null(RemoteConnectRules.TokenError(" ghp_x "));

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void HostError_Blank_AsksForTheHost(string? host) =>
        Assert.Equal("Enter the IP address or hostname", RemoteConnectRules.HostError(host));

    [Fact]
    public void HostError_Present_ReturnsNull() => Assert.Null(RemoteConnectRules.HostError("192.0.2.1"));

    [Theory]
    [InlineData("1")]
    [InlineData("22")]
    [InlineData("65535")]
    [InlineData(" 2222 ")]
    public void PortError_InRange_ReturnsNull(string port) => Assert.Null(RemoteConnectRules.PortError(port));

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("22.5")]
    [InlineData("")]
    [InlineData(null)]
    public void PortError_OutOfRangeOrNotANumber_ReturnsTheWebMessage(string? port) =>
        Assert.Equal("Enter a port between 1 and 65535", RemoteConnectRules.PortError(port));

    [Theory]
    [InlineData("22", 22)]
    [InlineData("65535", 65535)]
    public void TryParsePort_Valid_ReturnsThePort(string text, int expected)
    {
        Assert.True(RemoteConnectRules.TryParsePort(text, out var port));
        Assert.Equal(expected, port);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void UsernameError_Blank_AsksForTheUsername(string? username) =>
        Assert.Equal("Enter the SSH username", RemoteConnectRules.UsernameError(username));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void KeyFileError_Blank_AsksForTheKeyFile(string? path) =>
        Assert.Equal("Pick the SSH private key file", RemoteConnectRules.KeyFileError(path));

    [Theory]
    [InlineData("/")]
    [InlineData("~")]
    [InlineData("~/x")]
    [InlineData("/var/data")]
    [InlineData("  /var/data  ")]
    public void FolderError_AbsoluteOrHome_ReturnsNull(string folder) => Assert.Null(RemoteConnectRules.FolderError(folder));

    [Theory]
    [InlineData("rel/x")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("~user/x")]
    [InlineData("C:\\data")]
    public void FolderError_Relative_ReturnsTheWebMessage(string? folder) =>
        Assert.Equal("Enter an absolute path (e.g. /var/data) or start with ~", RemoteConnectRules.FolderError(folder));

    [Fact]
    public void ValidateTokenForm_BothValid_ReturnsTheParsedOrganizationAndTrimmedToken()
    {
        var result = RemoteConnectRules.ValidateTokenForm(SourceType.Github, "https://github.com/orgs/octo", "  ghp_x ");

        Assert.True(result.IsValid);
        Assert.Equal("octo", result.Organization!.Organization);
        Assert.Equal("ghp_x", result.Token);
    }

    [Fact]
    public void ValidateTokenForm_BothInvalid_ReportsBothErrors()
    {
        var result = RemoteConnectRules.ValidateTokenForm(SourceType.AzureDevops, "bad", " ");

        Assert.False(result.IsValid);
        Assert.NotNull(result.UrlError);
        Assert.NotNull(result.TokenError);
    }

    [Fact]
    public void ValidateSshForm_AllValid_HasNoErrors()
    {
        var errors = RemoteConnectRules.ValidateSshForm(new SshFormInput("host", "22", "alice", "/keys/id", "/var/data"));

        Assert.True(errors.IsValid);
    }

    [Fact]
    public void ValidateSshForm_Empty_ReportsEveryField()
    {
        var errors = RemoteConnectRules.ValidateSshForm(new SshFormInput(null, null, null, null, null));

        Assert.False(errors.IsValid);
        Assert.NotNull(errors.HostError);
        Assert.NotNull(errors.PortError);
        Assert.NotNull(errors.UsernameError);
        Assert.NotNull(errors.KeyFileError);
        Assert.NotNull(errors.FolderError);
    }

    [Fact]
    public void AuthMethodsFor_GitHub_AlwaysListsDeviceAppAndPat() =>
        Assert.Equal(
            [RemoteAuthMethod.GitHubDevice, RemoteAuthMethod.GitHubApp, RemoteAuthMethod.Pat],
            RemoteConnectRules.AuthMethodsFor(SourceType.Github));

    [Fact]
    public void AuthMethodsFor_AzureDevOps_IsPatOnly() =>
        Assert.Equal([RemoteAuthMethod.Pat], RemoteConnectRules.AuthMethodsFor(SourceType.AzureDevops));

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void IsMethodEnabled_FollowsAvailability_AndPatIsAlwaysEnabled(bool device, bool app)
    {
        var availability = new RemoteAuthAvailability(device, app);

        Assert.Equal(device, RemoteConnectRules.IsMethodEnabled(RemoteAuthMethod.GitHubDevice, availability));
        Assert.Equal(app, RemoteConnectRules.IsMethodEnabled(RemoteAuthMethod.GitHubApp, availability));
        Assert.True(RemoteConnectRules.IsMethodEnabled(RemoteAuthMethod.Pat, availability));
    }

    [Theory]
    [InlineData(true, true, RemoteAuthMethod.GitHubDevice)]
    [InlineData(true, false, RemoteAuthMethod.GitHubDevice)]
    [InlineData(false, true, RemoteAuthMethod.GitHubApp)]
    [InlineData(false, false, RemoteAuthMethod.Pat)]
    public void DefaultMethod_GitHub_IsTheFirstEnabledMethod(bool device, bool app, RemoteAuthMethod expected) =>
        Assert.Equal(expected, RemoteConnectRules.DefaultMethod(SourceType.Github, new RemoteAuthAvailability(device, app)));

    [Fact]
    public void DefaultMethod_AzureDevOps_IsPatEvenWhenGitHubMethodsAreConfigured() =>
        Assert.Equal(
            RemoteAuthMethod.Pat,
            RemoteConnectRules.DefaultMethod(SourceType.AzureDevops, new RemoteAuthAvailability(true, true)));

    [Fact]
    public void ValidateOAuthForm_ValidUrl_IsValidWithoutAToken()
    {
        var result = RemoteConnectRules.ValidateOAuthForm(SourceType.Github, "https://github.com/octo");

        Assert.True(result.IsValid);
        Assert.Null(result.UrlError);
    }

    [Fact]
    public void ValidateOAuthForm_InvalidUrl_ReturnsTheUrlError()
    {
        var result = RemoteConnectRules.ValidateOAuthForm(SourceType.Github, "nope");

        Assert.False(result.IsValid);
        Assert.Equal("Enter a URL like https://github.com/your-organization", result.UrlError);
    }
}
