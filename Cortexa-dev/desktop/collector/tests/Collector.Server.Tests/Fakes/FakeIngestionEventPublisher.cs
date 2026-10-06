using Collector.Server.Application.Events;
using Collector.Server.Application.Ports;

namespace Collector.Server.Tests.Fakes;

internal sealed class FakeIngestionEventPublisher : IIngestionEventPublisher
{
    public List<EventEnvelope> Published { get; } = [];

    public Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        Published.Add(envelope);
        return Task.CompletedTask;
    }
}
