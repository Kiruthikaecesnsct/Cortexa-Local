using Collector.Application.Ports;
using Collector.Domain.Enums;

namespace Collector.Application.Remote.Auth;

public sealed class RemoteAuthState : IRemoteAuthState
{
    private readonly Lock _lock = new();
    private readonly Dictionary<SourceType, RemoteAuthMethod> _methods = [];

    public event EventHandler? Changed;

    public RemoteAuthMethod MethodFor(SourceType provider)
    {
        lock (_lock)
        {
            return _methods.GetValueOrDefault(provider, RemoteAuthMethod.Pat);
        }
    }

    public void Set(SourceType provider, RemoteAuthMethod method)
    {
        if (Store(provider, method))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Reset(SourceType provider) => Set(provider, RemoteAuthMethod.Pat);

    private bool Store(SourceType provider, RemoteAuthMethod method)
    {
        lock (_lock)
        {
            var current = _methods.GetValueOrDefault(provider, RemoteAuthMethod.Pat);
            _methods[provider] = method;
            return current != method;
        }
    }
}
