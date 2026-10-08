using Collector.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Collector.Application.Upload;

public sealed record BatchSendOutcome(UploadBatchStatus Status, string? ServerBatchId, UploadError? Error);

public sealed class BatchSender(IKnowledgeUploadClient client, IBatchStore batchStore, ILogger<BatchSender> logger)
{
    public async Task<BatchSendOutcome> SendAsync(
        string localBatchId,
        string idempotencyKey,
        UploadPayload payload,
        CancellationToken cancellationToken)
    {
        await batchStore.MarkUploadingAsync(localBatchId, cancellationToken);
        try
        {
            var response = await client.UploadAsync(payload, idempotencyKey, cancellationToken);
            await batchStore.MarkUploadedAsync(localBatchId, response.BatchId, cancellationToken);
            return new BatchSendOutcome(UploadBatchStatus.Uploaded, response.BatchId, null);
        }
        catch (KnowledgeUploadException exception)
        {
            var error = UploadErrorMapper.Map(exception);
            logger.LogWarning("Upload of batch {BatchId} failed: {Error}.", localBatchId, UploadErrorMapper.Describe(error));
            await batchStore.MarkFailedAsync(localBatchId, UploadErrorMapper.Describe(error), cancellationToken);
            return new BatchSendOutcome(UploadBatchStatus.Failed, null, error);
        }
    }
}
