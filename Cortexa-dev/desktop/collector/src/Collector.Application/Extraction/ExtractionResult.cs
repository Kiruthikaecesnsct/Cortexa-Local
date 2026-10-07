using Collector.Domain.Enums;

namespace Collector.Application.Extraction;

public sealed record ExtractionResult
{
    public required string SourcePath { get; init; }

    public string? DocumentId { get; init; }

    public required DocumentStatus Status { get; init; }

    public string? Reason { get; init; }
}
