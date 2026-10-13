using Collector.Application.Remote.Auth;
using Collector.Domain.Enums;

namespace Collector.Application.Ports;

public interface IRemoteTokenSource
{
    SourceType Provider { get; }

    RemoteAuthMethod Method { get; }

    Task<RemoteAccessToken?> GetAsync(CancellationToken cancellationToken);
}
