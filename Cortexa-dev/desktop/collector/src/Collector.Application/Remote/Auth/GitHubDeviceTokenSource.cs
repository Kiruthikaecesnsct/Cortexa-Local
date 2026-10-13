using Collector.Application.Ports;
using Collector.Domain.Enums;

namespace Collector.Application.Remote.Auth;

public sealed class GitHubDeviceTokenSource(IOAuthTokenVault vault) : IRemoteTokenSource
{
    public SourceType Provider => SourceType.Github;

    public RemoteAuthMethod Method => RemoteAuthMethod.GitHubDevice;

    public async Task<RemoteAccessToken?> GetAsync(CancellationToken cancellationToken)
    {
        var tokens = await vault.ReadAsync(Method, cancellationToken);
        return tokens is null ? null : new RemoteAccessToken(tokens.AccessToken, RemoteTokenKind.OAuth);
    }
}
