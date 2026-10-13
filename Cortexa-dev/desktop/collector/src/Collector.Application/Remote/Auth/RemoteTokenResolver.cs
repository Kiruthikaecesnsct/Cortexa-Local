using Collector.Application.Ports;
using Collector.Domain.Enums;

namespace Collector.Application.Remote.Auth;

public sealed class RemoteTokenResolver : IRemoteTokenResolver
{
    private readonly IRemoteAuthState _state;
    private readonly Dictionary<(SourceType Provider, RemoteAuthMethod Method), IRemoteTokenSource> _sources;

    public RemoteTokenResolver(IRemoteAuthState state, IEnumerable<IRemoteTokenSource> sources)
    {
        _state = state;
        _sources = sources.ToDictionary(source => (source.Provider, source.Method));
    }

    public Task<RemoteAccessToken?> ResolveAsync(SourceType provider, CancellationToken cancellationToken) =>
        _sources.TryGetValue((provider, _state.MethodFor(provider)), out var source)
            ? source.GetAsync(cancellationToken)
            : Task.FromResult<RemoteAccessToken?>(null);
}
