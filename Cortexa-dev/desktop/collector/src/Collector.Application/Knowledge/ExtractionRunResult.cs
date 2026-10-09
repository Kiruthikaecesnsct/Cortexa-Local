using Collector.Application.Ports;
using Collector.Domain.Enums;

namespace Collector.Application.Knowledge;

public sealed record ExtractionProgress(int CompletedUnits, int TotalUnits);

public sealed record ExtractionRunResult
{
    public required IReadOnlyList<ExtractedKnowledgeItem> Items { get; init; }

    public required int TotalUnits { get; init; }

    public required int FailedUnits { get; init; }

    public required int SkippedUnits { get; init; }

    public required IReadOnlyList<string> FailedDocumentIds { get; init; }

    public required CollectorProvider Provider { get; init; }

    public required string Model { get; init; }

    public required string PromptVersion { get; init; }

    public AiFailureKind? FailureKind { get; init; }

    public bool IsFailed => TotalUnits > 0 && FailedUnits == TotalUnits;
}

public sealed record ExtractionRunRequest(
    IReadOnlyList<string> DocumentIds,
    CollectorProvider Provider = CollectorProvider.Claude,
    string? Model = null);
