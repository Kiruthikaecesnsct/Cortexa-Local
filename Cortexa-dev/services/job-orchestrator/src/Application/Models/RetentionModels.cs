namespace Cortexa.JobOrchestrator.Application.Models;

public sealed record RetentionCandidate(
    string BatchId,
    string State,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? FailedAt,
    DateTimeOffset LastActivityUtc,
    string Reason);

public sealed record RetentionSweepAccumulator(
    IReadOnlyList<RetentionCandidate> Candidates,
    int DeletedCount,
    IReadOnlyDictionary<string, int> PerStoreDeleted,
    IReadOnlyList<string> Errors);

public sealed record RetentionSweepReport(
    DateTimeOffset StartedUtc,
    bool DryRun,
    IReadOnlyDictionary<string, int> PerStateFound,
    IReadOnlyList<RetentionCandidate> Candidates,
    int DeletedCount,
    IReadOnlyDictionary<string, int> PerStoreDeleted,
    IReadOnlyList<string> Errors,
    TimeSpan Elapsed);
