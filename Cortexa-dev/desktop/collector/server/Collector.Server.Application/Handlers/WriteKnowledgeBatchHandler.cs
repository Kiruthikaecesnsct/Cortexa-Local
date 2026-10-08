using Collector.Domain.Upload;
using Collector.Server.Application.Building;
using Collector.Server.Application.Commands;
using Collector.Server.Application.Errors;
using Collector.Server.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Collector.Server.Application.Handlers;

public sealed class WriteKnowledgeBatchHandler(
    IPipelineRowStore store,
    IngestionEventDispatcher dispatcher,
    BatchRollback rollback,
    IClock clock,
    ILogger<WriteKnowledgeBatchHandler> logger)
{
    public async Task<KnowledgeUploadResult> HandleAsync(
        WriteKnowledgeBatchCommand command,
        CancellationToken cancellationToken)
    {
        var plan = BatchPlanBuilder.Build(command, clock.UtcNow);

        await WriteRowsAsync(plan, cancellationToken);
        await dispatcher.PublishAsync(command.BatchId, plan.Documents, cancellationToken);

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

    private async Task WriteRowsAsync(BatchPlan plan, CancellationToken cancellationToken)
    {
        var stage = WriteStage.Documents;

        try
        {
            await store.UpsertDocumentsAsync(plan.Documents, cancellationToken);
            stage = WriteStage.Chunks;
            await store.UpsertChunksAsync(plan.Chunks, cancellationToken);
            stage = WriteStage.Provenance;
            await store.UpsertProvenanceAsync(plan.Provenance, cancellationToken);
            stage = WriteStage.Saga;
            await store.CreateSagaAsync(plan.Saga, cancellationToken);
        }
        catch (SagaAlreadyExistsException)
        {
            throw;
        }
        catch (Exception)
        {
            await rollback.RollBackAsync(plan, stage);
            throw;
        }
    }
}
