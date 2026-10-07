namespace Collector.Application.Extraction;

public sealed record Heading
{
    public required string Text { get; init; }

    public required int StartChar { get; init; }

    public required int EndChar { get; init; }
}
