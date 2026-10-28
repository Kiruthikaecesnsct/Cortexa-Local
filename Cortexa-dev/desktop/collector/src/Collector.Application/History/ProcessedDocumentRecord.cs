using Collector.Application.Candidates;
using Collector.Domain.Enums;

namespace Collector.Application.History;

public enum ProcessedStatus
{
    Succeeded,
    Failed,
    Canceled,
}

public sealed record ProcessedDocumentRecord
{
    public required string UserId { get; init; }

    public required string DocumentId { get; init; }

    public required string Filename { get; init; }

    public required SourceType SourceType { get; init; }

    public required SourceKind SourceKind { get; init; }

    public required string SourcePath { get; init; }

    public required long SizeBytes { get; init; }

    public required string ContentHash { get; init; }

    public required string Provider { get; init; }

    public required string Model { get; init; }

    public required ProcessedStatus Status { get; init; }

    public CandidateOutcome Outcome { get; init; }

    public string? FailureReason { get; init; }

    public IReadOnlyList<InventionCandidate> Candidates { get; init; } = [];

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public bool CanRetry => Status == ProcessedStatus.Failed;
}
