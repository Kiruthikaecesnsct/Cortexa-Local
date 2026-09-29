namespace Cortexa.JobOrchestrator.Application.Exceptions;

public sealed class ConcurrencyConflictException : Exception
{
    public string EntityId { get; }

    public ConcurrencyConflictException(string entityId)
        : base($"Concurrency conflict on entity '{entityId}'. ETag mismatch — safe to retry.")
    {
        EntityId = entityId;
    }
}
