using Collector.Domain.Upload;
using Collector.Server.Application.Building;
using Collector.Server.Application.Commands;
using Collector.Server.Application.Errors;
using Collector.Server.Application.Ports;
using Collector.Server.Application.Rows;
using Microsoft.Extensions.Logging;

namespace Collector.Server.Application.Handlers;

public sealed class WriteKnowledgeBatchHandler(
    IPipelineRowStore store,
    IIngestionEventPublisher publisher,
    IClock clock,
    ILogger<WriteKnowledgeBatchHandler> logger)
{
    private const string PublishStage = "publish";

    public async Task<KnowledgeUploadResult> HandleAsync(
        WriteKnowledgeBatchCommand command,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var plan = BuildPlan(command, now);

        await store.UpsertDocumentsAsync(plan.Documents, cancellationToken);
        await store.UpsertChunksAsync(plan.Chunks, cancellationToken);
        await store.UpsertProvenanceAsync(plan.Provenance, cancellationToken);
        await store.CreateSagaAsync(plan.Saga, cancellationToken);
        await PublishAsync(command.BatchId, plan.Documents, now, cancellationToken);

        logger.LogInformation(
            "Wrote batch {BatchId}: {DocumentCount} documents, {ChunkCount} chunks.",
            command.BatchId,
            plan.Documents.Count,
            plan.Chunks.Count);

        return new KnowledgeUploadResult
        {
            BatchId = command.BatchId,
            DocumentIds = [.. plan.Documents.Select(document => document.Id)]
        };
    }

    private static BatchPlan BuildPlan(WriteKnowledgeBatchCommand command, DateTimeOffset now)
    {
        var documents = new List<DocumentRow>(command.Documents.Count);
        var chunks = new List<ChunkRow>();
        var provenance = new List<ProvenanceRow>();

        foreach (var input in command.Documents)
        {
            var documentChunks = ChunkRowBuilder.Build(command.BatchId, input, command.Collector);
            documents.Add(DocumentRowBuilder.Build(command.BatchId, input, documentChunks.Count, now));
            chunks.AddRange(documentChunks);
            provenance.AddRange(ProvenanceRowBuilder.Build(documentChunks, input));
        }

        return new BatchPlan(documents, chunks, provenance, SagaRowBuilder.Build(command));
    }

    private async Task PublishAsync(
        string batchId,
        IReadOnlyList<DocumentRow> documents,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString();
        var published = 0;

        try
        {
            foreach (var document in documents)
            {
                var envelope = IngestionCompletedEventBuilder.Build(document, correlationId, now);
                await publisher.PublishAsync(envelope, cancellationToken);
                published++;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                "Publish failed for batch {BatchId} after {Published} of {Total} events.",
                batchId,
                published,
                documents.Count);
            throw new PipelineWriteException(batchId, PublishStage, exception);
        }
    }

    private sealed record BatchPlan(
        IReadOnlyList<DocumentRow> Documents,
        IReadOnlyList<ChunkRow> Chunks,
        IReadOnlyList<ProvenanceRow> Provenance,
        SagaRow Saga);
}
