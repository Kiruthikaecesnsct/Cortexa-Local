using Collector.Application.Ports;
using Collector.Domain.Enums;

namespace Collector.Infrastructure.Remote;

public sealed class RemoteRepositoryClients(IEnumerable<IRemoteRepositoryClient> clients) : IRemoteRepositoryClients
{
    private readonly Dictionary<SourceType, IRemoteRepositoryClient> _clients =
        clients.ToDictionary(client => client.Provider);

    public IRemoteRepositoryClient For(SourceType provider) =>
        _clients.TryGetValue(provider, out var client)
            ? client
            : throw new NotSupportedException($"Source {provider} has no remote repository client.");
}
