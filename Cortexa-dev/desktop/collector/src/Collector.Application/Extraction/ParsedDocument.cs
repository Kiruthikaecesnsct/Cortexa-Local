namespace Collector.Application.Extraction;

public sealed record PageSpan
{
    public required int PageNumber { get; init; }

    public required int StartOffset { get; init; }

    public required int EndOffset { get; init; }
}

public sealed record ParsedDocument
{
    public required string Text { get; init; }

    public IReadOnlyList<PageSpan> Pages { get; init; } = [];
}
