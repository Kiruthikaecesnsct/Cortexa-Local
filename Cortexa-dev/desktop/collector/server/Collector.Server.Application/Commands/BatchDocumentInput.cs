using Collector.Domain.Enums;
using Collector.Domain.Knowledge;

namespace Collector.Server.Application.Commands;

public sealed record BatchDocumentInput
{
    public required string DocumentId { get; init; }

    public required string Filename { get; init; }

    public required SourceKind SourceKind { get; init; }

    public IReadOnlyList<KnowledgeItem> Items { get; init; } = [];
}
