namespace Collector.Application.Knowledge;

public sealed record KnowledgePdfReport
{
    public required string GeneratedBy { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public required string Provider { get; init; }

    public required string Model { get; init; }

    public required IReadOnlyList<ExtractedKnowledgeItem> Items { get; init; }
}
