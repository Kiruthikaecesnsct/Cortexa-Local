using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Application.Upload;
using Collector.Domain.Enums;
using Collector.Tests.Extraction;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Upload;

public sealed class UploadKnowledgeHandlerTests
{
    private const int FiftyOneDocuments = 51;
    private const int HundredAndOneDocuments = 101;
    private const string Model = "claude-test";
    private const string PromptVersion = "knowledge.v1";
    private const string AppVersion = "1.2.3";

    private readonly InMemoryDocumentStore _documents = new();
    private readonly InMemoryBatchStore _batches = new();
    private readonly FakeUploadClient _client = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-07T09:30:00Z"));

    private UploadKnowledgeHandler Handler() => new(
        _documents,
        _batches,
        _client,
        new UploadBatchPlanner(),
        _time,
        NullLogger<UploadKnowledgeHandler>.Instance);

    private List<ExtractedKnowledgeItem> Items(int documentCount, bool register = true)
    {
        var items = new List<ExtractedKnowledgeItem>();
        for (var index = 0; index < documentCount; index++)
        {
            var document = TestData.Document($"doc-{index}");
            if (register)
            {
                _documents.ByPath[document.SourcePath] = document;
            }

            items.Add(TestData.Item($"Idea {index}", documentId: document.Id));
        }

        return items;
    }

    private static UploadRequest Request(IReadOnlyList<ExtractedKnowledgeItem> items, string? name = "Weekly upload") => new()
    {
        Items = items,
        Provider = CollectorProvider.Claude,
        Model = Model,
        PromptVersion = PromptVersion,
        AppVersion = AppVersion,
        BatchName = name,
    };

    private static KnowledgeUploadException Network() => new(null, null, "unreachable");

    [Fact]
    public async Task PrepareAsync_TwoBatches_CreatesOneRowEachWithGuidV7Key()
    {
        var session = await Handler().PrepareAsync(Request(Items(FiftyOneDocuments)), TestSupport.Ct);

        Assert.Equal(2, session.Results.Count);
        Assert.Equal(2, _batches.Rows.Count);
        Assert.All(_batches.Rows, row => Assert.Equal(7, Guid.Parse(row.Batch.IdempotencyKey).Version));
        Assert.Equal(2, _batches.Rows.Select(row => row.Batch.IdempotencyKey).Distinct().Count());
    }

    [Fact]
    public async Task PrepareAsync_Rows_CarryBatchMetadataAndDocumentIds()
    {
        await Handler().PrepareAsync(Request(Items(2)), TestSupport.Ct);

        var row = Assert.Single(_batches.Rows);
        Assert.Equal("Weekly upload", row.Batch.BatchName);
        Assert.Equal(Model, row.Batch.Model);
        Assert.Equal(PromptVersion, row.Batch.PromptVersion);
        Assert.Equal(["doc-0", "doc-1"], row.Batch.DocumentIds);
    }

    [Fact]
    public async Task PrepareAsync_NoBatchName_UsesTimestampedDefault()
    {
        await Handler().PrepareAsync(Request(Items(1), name: null), TestSupport.Ct);

        Assert.Equal("Collector upload 2026-10-07 09:30", Assert.Single(_batches.Rows).Batch.BatchName);
    }

    [Fact]
    public async Task UploadPendingAsync_AllBatches_UploadsWithCollectorInfoAndMarksRowsUploaded()
    {
        var session = await Handler().PrepareAsync(Request(Items(FiftyOneDocuments)), TestSupport.Ct);

        await session.UploadPendingAsync(null, TestSupport.Ct);

        Assert.True(session.IsComplete);
        Assert.Equal(AppVersion, _client.Calls[0].Request.Collector.AppVersion);
        Assert.All(_batches.Rows, row => Assert.Equal([BatchStatus.Draft, BatchStatus.Uploading, BatchStatus.Uploaded], row.History));
        Assert.Equal(["server-1", "server-2"], _batches.Rows.Select(row => row.ServerBatchId));
    }

    [Fact]
    public async Task UploadPendingAsync_FailedBatchRetried_ResendsOnlyItWithSameKeyAndBody()
    {
        _client.SucceedNext().FailNext(Network());
        var session = await Handler().PrepareAsync(Request(Items(FiftyOneDocuments)), TestSupport.Ct);
        await session.UploadPendingAsync(null, TestSupport.Ct);

        await session.UploadPendingAsync(null, TestSupport.Ct);

        Assert.Equal(3, _client.Calls.Count);
        var firstAttempt = _client.Calls[1];
        var retry = _client.Calls[2];
        Assert.Equal(firstAttempt.Key, retry.Key);
        Assert.Same(firstAttempt.Request, retry.Request);
        Assert.Equal(firstAttempt.Body, retry.Body);
        Assert.Equal(1, _client.Calls.Count(call => call.Key == _client.Calls[0].Key));
        Assert.True(session.IsComplete);
    }

    [Fact]
    public async Task UploadPendingAsync_FailureThenRetry_RowTransitionsThroughFailedToUploaded()
    {
        _client.FailNext(Network());
        var session = await Handler().PrepareAsync(Request(Items(1)), TestSupport.Ct);

        await session.UploadPendingAsync(null, TestSupport.Ct);
        var afterFailure = Assert.Single(_batches.Rows);
        var failedError = afterFailure.LastError;
        await session.UploadPendingAsync(null, TestSupport.Ct);

        Assert.Equal("network", failedError);
        Assert.Equal(
            [BatchStatus.Draft, BatchStatus.Uploading, BatchStatus.Failed, BatchStatus.Uploading, BatchStatus.Uploaded],
            afterFailure.History);
        Assert.Equal("server-1", afterFailure.ServerBatchId);
        Assert.Null(afterFailure.LastError);
    }

    [Fact]
    public async Task UploadPendingAsync_RejectedByServer_StoresDescribedError()
    {
        _client.FailNext(new KnowledgeUploadException(400, "validation_failed", "bad"));
        var session = await Handler().PrepareAsync(Request(Items(1)), TestSupport.Ct);

        var results = await session.UploadPendingAsync(null, TestSupport.Ct);

        Assert.Equal("rejected:validation_failed", Assert.Single(_batches.Rows).LastError);
        Assert.Equal(new UploadError(UploadErrorKind.Rejected, "validation_failed"), Assert.Single(results).Error);
        Assert.True(session.HasFailures);
    }

    [Fact]
    public async Task UploadPendingAsync_SessionExpired_StopsAndLeavesRemainingPending()
    {
        _client.FailNext(new KnowledgeUploadException(401, null, "expired"));
        var session = await Handler().PrepareAsync(Request(Items(HundredAndOneDocuments)), TestSupport.Ct);

        var results = await session.UploadPendingAsync(null, TestSupport.Ct);

        Assert.Single(_client.Calls);
        Assert.Equal(
            [UploadBatchStatus.Failed, UploadBatchStatus.Pending, UploadBatchStatus.Pending],
            results.Select(result => result.Status));
        Assert.Equal([BatchStatus.Draft, BatchStatus.Draft], _batches.Rows.Skip(1).Select(row => row.Status));
        Assert.False(session.IsComplete);
    }

    [Fact]
    public async Task UploadPendingAsync_Progress_ReportsEachAttemptedBatch()
    {
        var session = await Handler().PrepareAsync(Request(Items(FiftyOneDocuments)), TestSupport.Ct);
        var progress = new RecordingProgress<BatchUploadResult>();

        await session.UploadPendingAsync(progress, TestSupport.Ct);

        Assert.Equal([0, 1], progress.Reports.Select(report => report.Index));
    }

    [Fact]
    public async Task PrepareAsync_DocumentOverItemLimit_BlockedWhileOthersUpload()
    {
        var items = Items(2);
        items.AddRange(Enumerable.Range(0, UploadLimitsMirror.MaxItemsPerDocument + 1).Select(index => TestData.Item($"Many {index}", documentId: "doc-1")));
        var session = await Handler().PrepareAsync(Request(items), TestSupport.Ct);

        await session.UploadPendingAsync(null, TestSupport.Ct);

        var blocked = Assert.Single(session.Blocked);
        Assert.Equal(("doc-1", BlockReason.TooManyItems), (blocked.DocumentId, blocked.Reason));
        Assert.Equal(["doc-0"], Assert.Single(_client.Calls).Request.Documents.Select(document => document.ClientDocumentId));
    }

    [Fact]
    public async Task PrepareAsync_DocumentMissingFromStore_ReportedAsBlockedWhileOthersUpload()
    {
        var items = Items(1);
        items.AddRange(Items(1, register: false).Select(item => item with { DocumentId = "ghost", DocumentName = "ghost.cs" }));
        var session = await Handler().PrepareAsync(Request(items), TestSupport.Ct);

        await session.UploadPendingAsync(null, TestSupport.Ct);

        var blocked = Assert.Single(session.Blocked);
        Assert.Equal(new BlockedDocument("ghost", "ghost.cs", 1, BlockReason.DocumentMissing), blocked);
        Assert.Equal("doc-0", Assert.Single(Assert.Single(_client.Calls).Request.Documents).ClientDocumentId);
    }

    [Fact]
    public async Task UploadPendingAsync_ClientCancelled_PropagatesOperationCanceled()
    {
        _client.FailNext(new OperationCanceledException());
        var session = await Handler().PrepareAsync(Request(Items(1)), TestSupport.Ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.UploadPendingAsync(null, TestSupport.Ct));
    }
}
