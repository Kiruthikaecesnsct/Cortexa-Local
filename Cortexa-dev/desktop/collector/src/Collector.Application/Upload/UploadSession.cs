namespace Collector.Application.Upload;

public sealed class UploadSession
{
    private readonly BatchSender _sender;
    private readonly List<SessionBatch> _batches;

    internal UploadSession(
        IReadOnlyList<PlannedBatchRecord> planned,
        IReadOnlyList<BlockedDocument> blocked,
        BatchSender sender)
    {
        _sender = sender;
        _batches = [.. planned.Select(record => new SessionBatch(record))];
        Blocked = blocked;
    }

    public IReadOnlyList<BlockedDocument> Blocked { get; }

    public IReadOnlyList<string> LocalBatchIds => [.. _batches.Select(batch => batch.Record.LocalBatchId)];

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
        var outcome = await _sender.SendAsync(
            batch.Record.LocalBatchId,
            batch.Record.IdempotencyKey,
            batch.Record.Payload,
            cancellationToken);
        batch.Result = batch.Result with { Status = outcome.Status, ServerBatchId = outcome.ServerBatchId, Error = outcome.Error };
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

internal sealed record PlannedBatchRecord(PlannedBatch Planned, string IdempotencyKey, string LocalBatchId, UploadPayload Payload);
