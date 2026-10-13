using Collector.Application.Remote.Auth;
using Collector.Domain.Enums;

namespace Collector.Application.Ports;

public interface IOAuthTokenVault
{
    Task<OAuthTokenSet?> ReadAsync(RemoteAuthMethod method, CancellationToken cancellationToken);

    Task SaveAsync(RemoteAuthMethod method, OAuthTokenSet tokens, CancellationToken cancellationToken);

    Task DeleteAsync(RemoteAuthMethod method, CancellationToken cancellationToken);

    Task DeleteProviderAsync(SourceType provider, CancellationToken cancellationToken);

    void DropCache();
}
