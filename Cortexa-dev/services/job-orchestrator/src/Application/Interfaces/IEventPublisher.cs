using Cortexa.JobOrchestrator.Application.Contracts;

namespace Cortexa.JobOrchestrator.Application.Interfaces;

public interface IEventPublisher
{
    Task PublishAsync(EventEnvelope envelope, CancellationToken ct);
}
