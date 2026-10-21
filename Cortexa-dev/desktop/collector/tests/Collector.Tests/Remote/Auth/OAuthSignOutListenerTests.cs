using Collector.Application.Auth;
using Collector.Application.Remote.Auth;
using Collector.Domain.Enums;
using Collector.Tests.Support;

namespace Collector.Tests.Remote.Auth;

public sealed class OAuthSignOutListenerTests
{
    private readonly FakeSession _session = new() { Current = SessionState.SignedIn };
    private readonly InMemoryOAuthVault _vault = new();
    private readonly RemoteAuthState _state = new();

    private void Arrange()
    {
        _ = new OAuthSignOutListener(_session, _vault, _state);
        _state.Set(SourceType.Github, RemoteAuthMethod.GitHubDevice);
        _state.Set(SourceType.AzureDevops, RemoteAuthMethod.EntraId);
    }

    [Fact]
    public void SessionChanged_SignedOut_ResetsGitHubAndAzureDevOps()
    {
        Arrange();
        _session.Current = SessionState.SignedOut;

        _session.RaiseChanged();

        Assert.Equal(RemoteAuthMethod.Pat, _state.MethodFor(SourceType.Github));
        Assert.Equal(RemoteAuthMethod.Pat, _state.MethodFor(SourceType.AzureDevops));
        Assert.Equal(1, _vault.DropCalls);
    }

    [Fact]
    public void SessionChanged_StillSignedIn_KeepsBothStates()
    {
        Arrange();

        _session.RaiseChanged();

        Assert.Equal(RemoteAuthMethod.GitHubDevice, _state.MethodFor(SourceType.Github));
        Assert.Equal(RemoteAuthMethod.EntraId, _state.MethodFor(SourceType.AzureDevops));
    }
}
