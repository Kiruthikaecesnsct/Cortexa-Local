using Collector.Domain.Enums;

namespace Collector.Domain.Extraction;

public sealed record ExtractionUnit
{
    public required string Id { get; init; }

    public required string DocumentId { get; init; }

    public required int Ordinal { get; init; }

    public required UnitKind UnitKind { get; init; }

    public int? PageNumber { get; init; }

    public string? SectionTitle { get; init; }

    public string? FilePath { get; init; }

    public int? StartLine { get; init; }

    public int? EndLine { get; init; }

    public required string Text { get; init; }

    public required int TokenCount { get; init; }

    public required DocumentStatus Status { get; init; }
}
