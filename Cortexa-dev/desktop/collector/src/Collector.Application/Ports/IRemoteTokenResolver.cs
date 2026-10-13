using Collector.Application.Remote.Auth;
using Collector.Domain.Enums;

namespace Collector.Application.Ports;

public interface IRemoteTokenResolver
{
    Task<RemoteAccessToken?> ResolveAsync(SourceType provider, CancellationToken cancellationToken);
}
