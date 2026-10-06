using Collector.Server.Application.Events;

namespace Collector.Server.Application.Ports;

public interface IIngestionEventPublisher
{
    Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken);
}
