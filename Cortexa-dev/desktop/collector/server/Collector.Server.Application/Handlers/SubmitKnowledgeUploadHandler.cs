using Collector.Domain.Upload;
using Collector.Server.Application.Commands;
using Collector.Server.Application.Errors;
using Collector.Server.Application.Ports;
using Collector.Server.Application.Upload;

namespace Collector.Server.Application.Handlers;

public sealed class SubmitKnowledgeUploadHandler(
    IPipelineRowStore store,
    UploadPreparer preparer,
    WriteKnowledgeBatchHandler writer,
    UploadReplayHandler replayer)
{
    public async Task<UploadOutcome> HandleAsync(
        SubmitKnowledgeUploadCommand command,
        CancellationToken cancellationToken)
    {
        var batchId = DeterministicIds.BatchId(command.Caller.UserId, command.IdempotencyKey);
        var fingerprint = RequestFingerprint.Compute(command.Request);
        var existing = await store.GetSagaAsync(batchId, cancellationToken);
        if (existing is not null)
        {
            return await replayer.ReplayAsync(existing, fingerprint, cancellationToken);
        }

        var preparation = await preparer.PrepareAsync(command.Request, command.Caller, batchId, cancellationToken);
        if (preparation.Command is null)
        {
            return UploadOutcome.Invalid(preparation.Errors);
        }

        return await WriteAsync(Stamp(preparation.Command, fingerprint), cancellationToken);
    }

    private static WriteKnowledgeBatchCommand Stamp(WriteKnowledgeBatchCommand command, string fingerprint) =>
        command with { Saga = command.Saga with { RequestFingerprint = fingerprint } };

    private async Task<UploadOutcome> WriteAsync(
        WriteKnowledgeBatchCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await writer.HandleAsync(command, cancellationToken);
            return UploadOutcome.Created(result);
        }
        catch (SagaAlreadyExistsException)
        {
            var winner = await store.GetSagaAsync(command.BatchId, cancellationToken);
            return winner is null
                ? throw new SagaAlreadyExistsException(command.BatchId)
                : await replayer.ReplayWithoutPublishAsync(winner);
        }
    }
}
