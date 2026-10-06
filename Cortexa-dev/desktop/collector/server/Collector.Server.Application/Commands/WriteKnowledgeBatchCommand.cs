using Collector.Domain.Upload;

namespace Collector.Server.Application.Commands;

public sealed record WriteKnowledgeBatchCommand
{
    public required string BatchId { get; init; }

    public required SagaMetadataInput Saga { get; init; }

    public required CollectorInfo Collector { get; init; }

    public IReadOnlyList<BatchDocumentInput> Documents { get; init; } = [];
}
