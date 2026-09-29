using Cortexa.JobOrchestrator.Application.Models;

namespace Cortexa.JobOrchestrator.Application.Interfaces;

public interface IBatchRetentionQuery
{
    Task<IReadOnlyList<RetentionCandidate>> FindFailedOlderThanAsync(
        DateTimeOffset cutoffUtc,
        int page,
        CancellationToken ct);

    Task<IReadOnlyList<RetentionCandidate>> FindStuckIncompleteAsync(
        DateTimeOffset cutoffUtc,
        int page,
        CancellationToken ct);

    Task<IReadOnlyList<RetentionCandidate>> FindStuckInProgressAsync(
        DateTimeOffset cutoffUtc,
        int page,
        CancellationToken ct);
}
