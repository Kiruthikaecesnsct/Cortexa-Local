using Collector.Domain.Enums;
using Collector.Domain.Knowledge;

namespace Collector.Application.Knowledge;

public sealed record ExtractedKnowledgeItem
{
    public required string DocumentId { get; init; }

    public required string DocumentName { get; init; }

    public required string DocumentPath { get; init; }

    public required UnitKind UnitKind { get; init; }

    public required KnowledgeKind Kind { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public string? Details { get; init; }

    public required KnowledgeSource Source { get; init; }

    public string? Excerpt { get; init; }

    public required EchoVerdict EchoVerdict { get; init; }

    public required string PromptVersion { get; init; }
}
