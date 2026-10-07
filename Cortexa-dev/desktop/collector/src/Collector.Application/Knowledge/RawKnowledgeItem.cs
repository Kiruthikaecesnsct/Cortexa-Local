using Collector.Domain.Enums;

namespace Collector.Application.Knowledge;

public sealed record RawKnowledgeItem
{
    public required KnowledgeKind Kind { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public string? Details { get; init; }

    public string? AnchorQuote { get; init; }
}

public sealed record KnowledgeParseResult(bool Succeeded, IReadOnlyList<RawKnowledgeItem> Items, int DroppedItems)
{
    public static KnowledgeParseResult Failure { get; } = new(false, [], 0);
}
