using Collector.Server.Application.Events;
using Collector.Server.Application.Ports;

namespace Collector.Server.Tests.Fakes;

internal sealed class FakeIngestionEventPublisher : IIngestionEventPublisher
{
    public List<EventEnvelope> Published { get; } = [];

    public int? FailAfter { get; set; }

    public Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (FailAfter is { } limit && Published.Count >= limit)
        {
            return Task.FromException(new InvalidOperationException("broker down"));
        }

        Published.Add(envelope);
        return Task.CompletedTask;
    }
}
