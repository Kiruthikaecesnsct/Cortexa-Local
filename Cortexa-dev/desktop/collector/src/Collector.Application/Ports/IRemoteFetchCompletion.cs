using Collector.Domain.Remote;

namespace Collector.Application.Ports;

public interface IRemoteFetchCompletion
{
    Task CompleteFetchAsync(RemoteRepository repository, string branch);
}
