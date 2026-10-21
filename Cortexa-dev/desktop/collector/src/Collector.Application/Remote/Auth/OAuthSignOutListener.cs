using Collector.Application.Auth;
using Collector.Application.Ports;
using Collector.Domain.Enums;

namespace Collector.Application.Remote.Auth;

public sealed class OAuthSignOutListener
{
    private readonly ISessionState _session;
    private readonly IOAuthTokenVault _vault;
    private readonly IRemoteAuthState _state;

    public OAuthSignOutListener(ISessionState session, IOAuthTokenVault vault, IRemoteAuthState state)
    {
        _session = session;
        _vault = vault;
        _state = state;
        _session.Changed += OnSessionChanged;
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (_session.Current == SessionState.SignedIn)
        {
            return;
        }

        _vault.DropCache();
        _state.Reset(SourceType.Github);
        _state.Reset(SourceType.AzureDevops);
    }
}
