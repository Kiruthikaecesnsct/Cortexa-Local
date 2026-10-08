using System.Text.Json;
using Collector.Application.Ports;
using Collector.Domain.Enums;
using Collector.Domain.Knowledge;
using Collector.Domain.Serialization;
using Collector.Domain.Upload;

namespace Collector.Tests.Support;

internal static class UploadData
{
    public static CollectorInfo Collector { get; } = new()
    {
        AppVersion = "1.0.0",
        Provider = CollectorProvider.Claude,
        Model = "claude-test",
        PromptVersion = "knowledge.v1",
    };

    public static UploadDocument Document(string id, int itemCount = 1, int detailsLength = 0) => new()
    {
        ClientDocumentId = id,
        Filename = $"{id}.cs",
        SourceKind = SourceKind.Code,
        SourceType = SourceType.Local,
        KnowledgeItems = [.. Enumerable.Range(0, itemCount).Select(index => Item(index, detailsLength))],
    };

    public static KnowledgeItem Item(int index, int detailsLength = 0) => new()
    {
        Kind = KnowledgeKind.Method,
        UnitKind = UnitKind.File,
        Title = $"Title {index}",
        Summary = "Summary text",
        Details = detailsLength > 0 ? new string('x', detailsLength) : null,
        Source = new KnowledgeSource { FilePath = TestData.FilePath, LineStart = 1, LineEnd = 1 },
    };

    public static int BodySize(KnowledgeUploadRequest request) =>
        JsonSerializer.SerializeToUtf8Bytes(request, CollectorJson.Options).Length;
}

internal sealed class InMemoryBatchStore : IBatchStore
{
    public sealed record Row(string Id, NewBatch Batch)
    {
        public BatchStatus Status { get; set; } = BatchStatus.Draft;

        public string? ServerBatchId { get; set; }

        public string? LastError { get; set; }

        public List<BatchStatus> History { get; } = [BatchStatus.Draft];
    }

    private readonly Dictionary<string, Row> _rows = [];

    public int FindByServerBatchIdCalls { get; private set; }

    public IReadOnlyList<Row> Rows => [.. _rows.Values];

    public Task<StoredBatch> CreateAsync(NewBatch batch, CancellationToken cancellationToken)
    {
        var id = $"local-{_rows.Count}";
        _rows[id] = new Row(id, batch);
        return Task.FromResult(new StoredBatch
        {
            Id = id,
            IdempotencyKey = batch.IdempotencyKey,
            BatchName = batch.BatchName,
            Status = BatchStatus.Draft,
            DocumentIds = batch.DocumentIds,
        });
    }

    public Task<StoredBatch?> FindByDocumentsAsync(IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken) =>
        Task.FromResult<StoredBatch?>(null);

    public Task<StoredBatch?> FindByServerBatchIdAsync(string serverBatchId, CancellationToken cancellationToken)
    {
        FindByServerBatchIdCalls++;
        var row = _rows.Values.FirstOrDefault(candidate => candidate.ServerBatchId == serverBatchId);
        return Task.FromResult<StoredBatch?>(row is null ? null : new StoredBatch
        {
            Id = row.Id,
            IdempotencyKey = row.Batch.IdempotencyKey,
            BatchName = row.Batch.BatchName,
            Status = row.Status,
            ServerBatchId = row.ServerBatchId,
            DocumentIds = row.Batch.DocumentIds,
        });
    }

    public Task MarkUploadingAsync(string batchId, CancellationToken cancellationToken)
    {
        Transition(batchId, BatchStatus.Uploading, null, null);
        return Task.CompletedTask;
    }

    public Task MarkUploadedAsync(string batchId, string serverBatchId, CancellationToken cancellationToken)
    {
        Transition(batchId, BatchStatus.Uploaded, serverBatchId, null);
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(string batchId, string error, CancellationToken cancellationToken)
    {
        Transition(batchId, BatchStatus.Failed, null, error);
        return Task.CompletedTask;
    }

    private void Transition(string batchId, BatchStatus status, string? serverBatchId, string? error)
    {
        var row = _rows[batchId];
        row.Status = status;
        row.ServerBatchId = serverBatchId ?? row.ServerBatchId;
        row.LastError = error;
        row.History.Add(status);
    }
}

internal sealed class FakeUploadClient : IKnowledgeUploadClient
{
    public sealed record Call(KnowledgeUploadRequest Request, string Key, byte[] Body);

    private readonly Queue<Exception?> _failures = new();
    private int _batchCounter;

    public List<Call> Calls { get; } = [];

    public FakeUploadClient FailNext(Exception exception)
    {
        _failures.Enqueue(exception);
        return this;
    }

    public FakeUploadClient SucceedNext()
    {
        _failures.Enqueue(null);
        return this;
    }

    public Task<KnowledgeUploadResult> UploadAsync(KnowledgeUploadRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        Calls.Add(new Call(request, idempotencyKey, JsonSerializer.SerializeToUtf8Bytes(request, CollectorJson.Options)));
        var failure = _failures.Count > 0 ? _failures.Dequeue() : null;
        if (failure is not null)
        {
            return Task.FromException<KnowledgeUploadResult>(failure);
        }

        return Task.FromResult(new KnowledgeUploadResult { BatchId = $"server-{++_batchCounter}" });
    }
}
