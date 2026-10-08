using Collector.Application.Ports;
using Collector.Application.Upload;
using Collector.Domain.Enums;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Tests.Upload;

public sealed class RetryFailedUploadHandlerTests
{
    private const string Key = "key-1";

    private readonly InMemoryBatchStore _batches = new();
    private readonly FakeUploadClient _client = new();

    private RetryFailedUploadHandler Handler() => Handler(_batches);

    private RetryFailedUploadHandler Handler(IBatchStore store) => new(
        store,
        new BatchSender(_client, store, NullLogger<BatchSender>.Instance),
        NullLogger<RetryFailedUploadHandler>.Instance);

    private static KnowledgeUploadException Network() => new(null, null, "unreachable");

    private async Task<(string Id, UploadPayload Payload)> FailedAsync(IBatchStore? store = null, string key = Key, string error = "network")
    {
        store ??= _batches;
        var batch = SqliteTestDatabase.NewBatch(key, "doc-0");
        var created = await store.CreateAsync(batch, TestSupport.Ct);
        await store.MarkFailedAsync(created.Id, error, TestSupport.Ct);
        return (created.Id, batch.Payload);
    }

    [Fact]
    public async Task RetryAsync_TwiceAfterFailures_SendsSameBytesAndKeyEachTime()
    {
        var (id, payload) = await FailedAsync();
        _client.FailNext(Network()).FailNext(Network()).SucceedNext();
        var handler = Handler();

        await handler.RetryAsync(id, TestSupport.Ct);
        await handler.RetryAsync(id, TestSupport.Ct);
        var third = await handler.RetryAsync(id, TestSupport.Ct);

        Assert.Equal(3, _client.Calls.Count);
        Assert.All(_client.Calls, call => Assert.Equal(payload.Body, call.Body));
        Assert.All(_client.Calls, call => Assert.Equal(Key, call.Key));
        Assert.Equal(UploadBatchStatus.Uploaded, third.Status);
    }

    [Fact]
    public async Task RetryAsync_Success_StoresServerBatchIdAndMarksUploaded()
    {
        var (id, _) = await FailedAsync();

        var result = await Handler().RetryAsync(id, TestSupport.Ct);

        var row = Assert.Single(_batches.Rows);
        Assert.True(result.Attempted);
        Assert.Equal("server-1", result.ServerBatchId);
        Assert.Equal(BatchStatus.Uploaded, row.Status);
        Assert.Equal("server-1", row.ServerBatchId);
        Assert.Null(row.LastError);
    }

    [Fact]
    public async Task RetryAsync_Failure_UpdatesLastErrorAndReportsError()
    {
        var (id, _) = await FailedAsync(error: "unknown");
        _client.FailNext(new KnowledgeUploadException(409, "upload_in_progress", "busy"));

        var result = await Handler().RetryAsync(id, TestSupport.Ct);

        var row = Assert.Single(_batches.Rows);
        Assert.Equal(BatchStatus.Failed, row.Status);
        Assert.Equal("inprogress", row.LastError);
        Assert.Equal(new UploadError(UploadErrorKind.InProgress, null), result.Error);
        Assert.True(result.Error!.CanRetry);
    }

    [Fact]
    public async Task RetryAsync_UploadedBatch_DoesNothing()
    {
        var (id, _) = await FailedAsync();
        await _batches.MarkUploadedAsync(id, "srv-9", TestSupport.Ct);

        var result = await Handler().RetryAsync(id, TestSupport.Ct);

        Assert.False(result.Attempted);
        Assert.Equal(UploadBatchStatus.Uploaded, result.Status);
        Assert.Equal("srv-9", result.ServerBatchId);
        Assert.Empty(_client.Calls);
    }

    [Fact]
    public async Task RetryAsync_ReplacedBatch_DoesNothing()
    {
        var (id, _) = await FailedAsync();
        await _batches.MarkReplacedAsync([id], TestSupport.Ct);

        var result = await Handler().RetryAsync(id, TestSupport.Ct);

        Assert.False(result.Attempted);
        Assert.Empty(_client.Calls);
        Assert.Equal(BatchStatus.Failed, Assert.Single(_batches.Rows).Status);
    }

    [Fact]
    public async Task RetryAsync_UnknownBatch_DoesNothing()
    {
        var result = await Handler().RetryAsync("nope", TestSupport.Ct);

        Assert.False(result.Attempted);
        Assert.Empty(_client.Calls);
    }

    [Fact]
    public async Task RetryAsync_AfterRestartRecovery_SendsStoredPayloadFromSqlite()
    {
        using var database = await SqliteTestDatabase.CreateAsync();
        var batch = SqliteTestDatabase.NewBatch(Key, await database.AddDocumentAsync("C:/a.cs"));
        var created = await database.Batches.CreateAsync(batch, TestSupport.Ct);
        await database.Batches.MarkUploadingAsync(created.Id, TestSupport.Ct);
        await database.Batches.MarkInterruptedAsync(TestSupport.Ct);
        var listed = Assert.Single(await database.Batches.ListRetryableAsync(TestSupport.Ct));

        var result = await Handler(database.Batches).RetryAsync(listed.Id, TestSupport.Ct);

        var call = Assert.Single(_client.Calls);
        Assert.Equal(batch.Payload.Body, call.Body);
        Assert.Equal(Key, call.Key);
        Assert.Equal(UploadBatchStatus.Uploaded, result.Status);
        Assert.Empty(await database.Batches.ListRetryableAsync(TestSupport.Ct));
    }
}
