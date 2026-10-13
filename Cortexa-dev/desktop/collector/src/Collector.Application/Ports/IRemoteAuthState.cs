using Collector.Application.Remote.Auth;
using Collector.Domain.Enums;

namespace Collector.Application.Ports;

public interface IRemoteAuthState
{
    event EventHandler? Changed;

    RemoteAuthMethod MethodFor(SourceType provider);

    void Set(SourceType provider, RemoteAuthMethod method);

    void Reset(SourceType provider);
}
