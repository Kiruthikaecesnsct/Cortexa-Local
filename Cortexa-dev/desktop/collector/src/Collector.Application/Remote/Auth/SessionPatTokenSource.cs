using Collector.Application.Ports;
using Collector.Domain.Enums;

namespace Collector.Application.Remote.Auth;

public sealed class SessionPatTokenSource(ISessionCredentials credentials, SourceType provider) : IRemoteTokenSource
{
    public SourceType Provider => provider;

    public RemoteAuthMethod Method => RemoteAuthMethod.Pat;

    public Task<RemoteAccessToken?> GetAsync(CancellationToken cancellationToken)
    {
        var pat = credentials.GetToken(provider);
        return Task.FromResult(string.IsNullOrWhiteSpace(pat)
            ? null
            : new RemoteAccessToken(pat, RemoteTokenKind.Pat));
    }
}
