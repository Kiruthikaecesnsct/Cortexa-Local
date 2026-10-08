using System.Collections.Concurrent;
using Collector.Application.Ports;
using Collector.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Collector.Application.Upload;

public sealed record RetryResult(bool Attempted, UploadBatchStatus Status, string? ServerBatchId, UploadError? Error)
{
    public static RetryResult Skipped(UploadBatchStatus status, string? serverBatchId = null) =>
        new(false, status, serverBatchId, null);
}

public sealed class RetryFailedUploadHandler(
    IBatchStore batchStore,
    BatchSender sender,
    ILogger<RetryFailedUploadHandler> logger)
{
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.Ordinal);

    public async Task<RetryResult> RetryAsync(string localBatchId, CancellationToken cancellationToken)
    {
        if (!_inFlight.TryAdd(localBatchId, 0))
        {
            return RetryResult.Skipped(UploadBatchStatus.Pending);
        }

        try
        {
            return await RetryCoreAsync(localBatchId, cancellationToken);
        }
        finally
        {
            _inFlight.TryRemove(localBatchId, out _);
        }
    }

    private async Task<RetryResult> RetryCoreAsync(string localBatchId, CancellationToken cancellationToken)
    {
        var batch = await batchStore.GetByIdAsync(localBatchId, cancellationToken);
        if (batch is null || batch.Status != BatchStatus.Failed || batch.IsReplaced)
        {
            return RetryResult.Skipped(StatusOf(batch), batch?.ServerBatchId);
        }

        var payload = await batchStore.GetPayloadAsync(localBatchId, cancellationToken);
        if (payload is null)
        {
            logger.LogWarning("Batch {BatchId} has no stored payload; retry skipped.", localBatchId);
            return RetryResult.Skipped(UploadBatchStatus.Failed);
        }

        var outcome = await sender.SendAsync(localBatchId, batch.IdempotencyKey, payload, cancellationToken);
        return new RetryResult(true, outcome.Status, outcome.ServerBatchId, outcome.Error);
    }

    private static UploadBatchStatus StatusOf(StoredBatch? batch) => batch?.Status switch
    {
        BatchStatus.Uploaded => UploadBatchStatus.Uploaded,
        BatchStatus.Failed => UploadBatchStatus.Failed,
        _ => UploadBatchStatus.Pending,
    };
}
