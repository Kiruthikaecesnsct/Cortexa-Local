using Collector.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Collector.Application.Upload;

public sealed class UploadSession
{
    private readonly IKnowledgeUploadClient _client;
    private readonly IBatchStore _batchStore;
    private readonly ILogger _logger;
    private readonly List<SessionBatch> _batches;

    internal UploadSession(
        IReadOnlyList<PlannedBatchRecord> planned,
        IReadOnlyList<BlockedDocument> blocked,
        IKnowledgeUploadClient client,
        IBatchStore batchStore,
        ILogger logger)
    {
        _client = client;
        _batchStore = batchStore;
        _logger = logger;
        _batches = [.. planned.Select(record => new SessionBatch(record))];
        Blocked = blocked;
    }

    public IReadOnlyList<BlockedDocument> Blocked { get; }

    public IReadOnlyList<BatchUploadResult> Results => [.. _batches.Select(batch => batch.Result)];

    public bool IsComplete => _batches.Count > 0 && _batches.All(batch => batch.Result.Status == UploadBatchStatus.Uploaded);

    public bool HasFailures => _batches.Any(batch => batch.Result.Status == UploadBatchStatus.Failed);

    public async Task<IReadOnlyList<BatchUploadResult>> UploadPendingAsync(
        IProgress<BatchUploadResult>? progress,
        CancellationToken cancellationToken)
    {
        foreach (var batch in _batches.Where(item => item.Result.Status != UploadBatchStatus.Uploaded))
        {
            await SendAsync(batch, cancellationToken);
            progress?.Report(batch.Result);
            if (batch.Result.Error?.Kind == UploadErrorKind.Session)
            {
                break;
            }
        }

        return Results;
    }

    private async Task SendAsync(SessionBatch batch, CancellationToken cancellationToken)
    {
        await _batchStore.MarkUploadingAsync(batch.Record.LocalBatchId, cancellationToken);
        try
        {
            var response = await _client.UploadAsync(batch.Record.Planned.Request, batch.Record.IdempotencyKey, cancellationToken);
            await _batchStore.MarkUploadedAsync(batch.Record.LocalBatchId, response.BatchId, cancellationToken);
            batch.Result = batch.Result with { Status = UploadBatchStatus.Uploaded, ServerBatchId = response.BatchId, Error = null };
        }
        catch (KnowledgeUploadException exception)
        {
            var error = UploadErrorMapper.Map(exception);
            _logger.LogWarning("Upload of batch {Index} failed: {Error}.", batch.Record.Planned.Index, UploadErrorMapper.Describe(error));
            await _batchStore.MarkFailedAsync(batch.Record.LocalBatchId, UploadErrorMapper.Describe(error), cancellationToken);
            batch.Result = batch.Result with { Status = UploadBatchStatus.Failed, Error = error };
        }
    }

    private sealed class SessionBatch(PlannedBatchRecord record)
    {
        public PlannedBatchRecord Record { get; } = record;

        public BatchUploadResult Result { get; set; } = new()
        {
            Index = record.Planned.Index,
            DocumentCount = record.Planned.DocumentCount,
            ItemCount = record.Planned.ItemCount,
            Status = UploadBatchStatus.Pending,
        };
    }
}

internal sealed record PlannedBatchRecord(PlannedBatch Planned, string IdempotencyKey, string LocalBatchId);
