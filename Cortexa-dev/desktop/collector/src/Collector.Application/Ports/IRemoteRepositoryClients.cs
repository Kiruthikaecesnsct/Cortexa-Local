using Collector.Domain.Enums;

namespace Collector.Application.Ports;

public interface IRemoteRepositoryClients
{
    IRemoteRepositoryClient For(SourceType provider);
}
