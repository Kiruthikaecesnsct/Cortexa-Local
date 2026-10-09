using Collector.Application.Remote;
using Collector.Domain.Enums;

namespace Collector.Tests.Remote;

public class RemoteFailureMessagesTests
{
    [Theory]
    [InlineData(SourceType.Github, "GitHub")]
    [InlineData(SourceType.AzureDevops, "Azure DevOps")]
    [InlineData(SourceType.Ssh, "SSH")]
    [InlineData(SourceType.CortexaRepo, "Cortexa")]
    public void For_ProviderSpecificFailures_NameTheProvider(SourceType provider, string expectedName)
    {
        RemoteFailureKind[] providerKinds =
        [
            RemoteFailureKind.MissingToken,
            RemoteFailureKind.Auth,
            RemoteFailureKind.AccessDenied,
            RemoteFailureKind.SsoRequired,
            RemoteFailureKind.NotFound,
            RemoteFailureKind.RateLimited,
            RemoteFailureKind.Upstream,
        ];

        Assert.All(providerKinds, kind => Assert.Contains(expectedName, RemoteFailureMessages.For(kind, provider), StringComparison.Ordinal));
    }

    [Fact]
    public void For_EveryKind_ReturnsDistinctNonEmptyMessage()
    {
        var kinds = Enum.GetValues<RemoteFailureKind>();

        var messages = kinds.Select(kind => RemoteFailureMessages.For(kind, SourceType.Github)).ToList();

        Assert.All(messages, message => Assert.False(string.IsNullOrWhiteSpace(message)));
        Assert.Equal(kinds.Length, messages.Distinct().Count());
    }

    [Fact]
    public void RemoteSourceException_Message_MatchesFailureMessage()
    {
        var exception = new RemoteSourceException(RemoteFailureKind.SsoRequired, SourceType.Github);

        Assert.Equal(RemoteFailureMessages.For(RemoteFailureKind.SsoRequired, SourceType.Github), exception.Message);
    }

    [Fact]
    public void DisplayName_UnmappedProvider_FallsBackToEnumName()
    {
        Assert.Equal("Cortexa", RemoteFailureMessages.DisplayName(SourceType.CortexaRepo));
    }

    [Fact]
    public void For_MissingToken_AsksForTheTokenWithoutPointingAtSettings()
    {
        var message = RemoteFailureMessages.For(RemoteFailureKind.MissingToken, SourceType.Github);

        Assert.Contains("GitHub", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings", message, StringComparison.Ordinal);
    }

    [Fact]
    public void For_CortexaAuth_TellsTheUserToSignInAgain()
    {
        var message = RemoteFailureMessages.For(RemoteFailureKind.Auth, SourceType.CortexaRepo);

        Assert.Contains("session expired", message, StringComparison.Ordinal);
        Assert.Contains("Sign in", message, StringComparison.Ordinal);
        Assert.DoesNotContain("token", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void For_CortexaAccessDenied_ExplainsMissingPermissionWithoutMentioningTokens()
    {
        var message = RemoteFailureMessages.For(RemoteFailureKind.AccessDenied, SourceType.CortexaRepo);

        Assert.Contains("permission", message, StringComparison.Ordinal);
        Assert.Contains("saved repositories", message, StringComparison.Ordinal);
        Assert.DoesNotContain("token", message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(RemoteFailureKind.NotFound)]
    [InlineData(RemoteFailureKind.RateLimited)]
    [InlineData(RemoteFailureKind.Upstream)]
    [InlineData(RemoteFailureKind.RepositoryTooLarge)]
    public void For_CortexaOtherKinds_UseTheGenericWording(RemoteFailureKind kind)
    {
        var cortexa = RemoteFailureMessages.For(kind, SourceType.CortexaRepo);

        Assert.Equal(RemoteFailureMessages.For(kind, SourceType.Github).Replace("GitHub", "Cortexa", StringComparison.Ordinal), cortexa);
    }

    [Fact]
    public void For_EveryKindForCortexa_ReturnsDistinctNonEmptyMessage()
    {
        var kinds = Enum.GetValues<RemoteFailureKind>();

        var messages = kinds.Select(kind => RemoteFailureMessages.For(kind, SourceType.CortexaRepo)).ToList();

        Assert.All(messages, message => Assert.False(string.IsNullOrWhiteSpace(message)));
        Assert.Equal(kinds.Length, messages.Distinct().Count());
    }
}
