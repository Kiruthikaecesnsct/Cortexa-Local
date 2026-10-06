using Collector.Domain.Upload;
using Collector.Server.Application.Commands;
using Collector.Server.Application.Errors;
using Collector.Server.Application.Ports;
using Collector.Server.Application.Rows;
using Collector.Server.Application.Upload;
using Microsoft.Extensions.Logging;

namespace Collector.Server.Application.Handlers;

public sealed class SubmitKnowledgeUploadHandler(
    IPipelineRowStore store,
    UploadPreparer preparer,
    WriteKnowledgeBatchHandler writer,
    ILogger<SubmitKnowledgeUploadHandler> logger)
{
    public async Task<UploadOutcome> HandleAsync(
        SubmitKnowledgeUploadCommand command,
        CancellationToken cancellationToken)
    {
        var batchId = DeterministicIds.BatchId(command.Caller.UserId, command.IdempotencyKey);
        var replay = await TryReplayAsync(batchId, command.Caller, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var preparation = await preparer.PrepareAsync(command.Request, command.Caller, batchId, cancellationToken);
        if (preparation.Command is null)
        {
            return UploadOutcome.Invalid(preparation.Errors);
        }

        return await WriteAsync(preparation.Command, command.Caller, cancellationToken);
    }

    private async Task<UploadOutcome> WriteAsync(
        WriteKnowledgeBatchCommand command,
        UploadCaller caller,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await writer.HandleAsync(command, cancellationToken);
            return UploadOutcome.Created(result);
        }
        catch (SagaAlreadyExistsException)
        {
            var replay = await TryReplayAsync(command.BatchId, caller, cancellationToken);
            return replay ?? throw new SagaAlreadyExistsException(command.BatchId);
        }
    }

    private async Task<UploadOutcome?> TryReplayAsync(
        string batchId,
        UploadCaller caller,
        CancellationToken cancellationToken)
    {
        var saga = await store.GetSagaAsync(batchId, cancellationToken);
        if (saga is null)
        {
            return null;
        }

        logger.LogInformation(
            "Replayed upload for batch {BatchId} by user {UserId}: {DocumentCount} documents.",
            batchId,
            caller.UserId,
            saga.Documents.Count);
        return UploadOutcome.Replayed(ToResult(saga));
    }

    private static KnowledgeUploadResult ToResult(SagaRow saga) =>
        new()
        {
            BatchId = saga.BatchId,
            DocumentIds = [.. saga.Documents.Select(document => document.DocumentId)]
        };
}
