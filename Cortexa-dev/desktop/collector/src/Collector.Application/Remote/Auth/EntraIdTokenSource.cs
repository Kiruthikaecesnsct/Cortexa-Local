using Collector.Application.Ports;
using Collector.Domain.Enums;

namespace Collector.Application.Remote.Auth;

public sealed class EntraIdTokenSource(IEntraIdAuthClient client) : IRemoteTokenSource
{
    public SourceType Provider => SourceType.AzureDevops;

    public RemoteAuthMethod Method => RemoteAuthMethod.EntraId;

    public async Task<RemoteAccessToken?> GetAsync(CancellationToken cancellationToken)
    {
        var outcome = await client.AcquireSilentAsync(cancellationToken);
        return outcome is EntraSignInOutcome.SignedIn signedIn
            ? new RemoteAccessToken(signedIn.AccessToken, RemoteTokenKind.OAuth)
            : null;
    }
}
