using Collector.Domain.Enums;

namespace Collector.Domain.Documents;

public sealed record CollectorDocument
{
    public required string Id { get; init; }

    public required SourceType SourceType { get; init; }

    public required SourceKind SourceKind { get; init; }

    public required string SourcePath { get; init; }

    public required string Filename { get; init; }

    public required string ContentHash { get; init; }

    public required long SizeBytes { get; init; }

    public required DocumentStatus Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
