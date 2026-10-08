using Collector.Application.Upload;
using Collector.Domain.Enums;
using Collector.Tests.Support;

namespace Collector.Tests.Cache;

public sealed class SqliteBatchStorePayloadTests : IAsyncLifetime
{
    private SqliteTestDatabase _database = null!;

    public async ValueTask InitializeAsync() => _database = await SqliteTestDatabase.CreateAsync();

    public ValueTask DisposeAsync()
    {
        _database.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<StoredBatchHandle> FailedBatchAsync(string key, params string[] fileNames)
    {
        var documents = new List<string>();
        foreach (var name in fileNames)
        {
            documents.Add(await _database.AddDocumentAsync($"C:/{name}"));
        }

        var batch = SqliteTestDatabase.NewBatch(key, [.. documents]);
        var created = await _database.Batches.CreateAsync(batch, TestSupport.Ct);
        await _database.Batches.MarkFailedAsync(created.Id, "network", TestSupport.Ct);
        return new StoredBatchHandle(created.Id, batch.Payload);
    }

    private async Task<long> CountAsync(string sql)
    {
        await using var connection = await _database.Connections.OpenAsync(TestSupport.Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestSupport.Ct));
    }

    [Fact]
    public async Task CreateAsync_StoresPayloadBytesAndHash()
    {
        var failed = await FailedBatchAsync("key-1", "a.cs");

        var stored = await _database.Batches.GetPayloadAsync(failed.Id, TestSupport.Ct);

        Assert.Equal(failed.Payload.Body, stored!.Body);
        Assert.Equal(failed.Payload.Sha256, stored.Sha256);
    }

    [Fact]
    public async Task CreateAsync_DocumentInsertFails_LeavesNoBatchOrPayload()
    {
        var batch = SqliteTestDatabase.NewBatch("key-1", "missing-document");

        await Assert.ThrowsAnyAsync<Exception>(() => _database.Batches.CreateAsync(batch, TestSupport.Ct));

        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM batches"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM batch_payloads"));
    }

    [Fact]
    public async Task GetPayloadAsync_UnknownBatch_ReturnsNull()
    {
        Assert.Null(await _database.Batches.GetPayloadAsync("nope", TestSupport.Ct));
    }

    [Fact]
    public async Task ListRetryableAsync_FailedBatch_ReturnsCountsAndError()
    {
        var failed = await FailedBatchAsync("key-1", "a.cs", "b.cs");

        var listed = Assert.Single(await _database.Batches.ListRetryableAsync(TestSupport.Ct));

        Assert.Equal(failed.Id, listed.Id);
        Assert.Equal("key-1", listed.IdempotencyKey);
        Assert.Equal("Batch key-1", listed.BatchName);
        Assert.Equal("network", listed.LastError);
        Assert.Equal(2, listed.DocumentCount);
        Assert.Equal(2, listed.ItemCount);
        Assert.Equal(_database.Time.GetUtcNow(), listed.UpdatedAt);
    }

    [Fact]
    public async Task ListRetryableAsync_NewestFirst()
    {
        var older = await FailedBatchAsync("older", "a.cs");
        _database.Time.Advance(TimeSpan.FromMinutes(5));
        var newer = await FailedBatchAsync("newer", "b.cs");

        var listed = await _database.Batches.ListRetryableAsync(TestSupport.Ct);

        Assert.Equal([newer.Id, older.Id], listed.Select(batch => batch.Id));
    }

    [Fact]
    public async Task ListRetryableAsync_ExcludesDraftUploadedReplacedAndPayloadless()
    {
        var draft = await _database.Batches.CreateAsync(
            SqliteTestDatabase.NewBatch("draft", await _database.AddDocumentAsync("C:/d.cs")), TestSupport.Ct);
        var uploaded = await FailedBatchAsync("uploaded", "u.cs");
        await _database.Batches.MarkUploadedAsync(uploaded.Id, "srv", TestSupport.Ct);
        var replaced = await FailedBatchAsync("replaced", "r.cs");
        await _database.Batches.MarkReplacedAsync([replaced.Id], TestSupport.Ct);
        var open = await FailedBatchAsync("open", "o.cs");
        await using (var connection = await _database.Connections.OpenAsync(TestSupport.Ct))
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO batches (id, idempotency_key, batch_name, provider, model, prompt_version, status, created_at, updated_at) " +
                "VALUES ('legacy', 'legacy-key', 'n', 'claude', 'm', 'p', 'failed', '2026-10-06T00:00:00Z', '2026-10-06T00:00:00Z')";
            await command.ExecuteNonQueryAsync(TestSupport.Ct);
        }

        var listed = await _database.Batches.ListRetryableAsync(TestSupport.Ct);

        Assert.Equal([open.Id], listed.Select(batch => batch.Id));
        Assert.NotEqual(draft.Id, open.Id);
    }

    [Fact]
    public async Task MarkReplacedAsync_FailedBatch_FlagsItAsReplaced()
    {
        var failed = await FailedBatchAsync("key-1", "a.cs");

        await _database.Batches.MarkReplacedAsync([failed.Id], TestSupport.Ct);

        Assert.True((await _database.Batches.GetByIdAsync(failed.Id, TestSupport.Ct))!.IsReplaced);
    }

    [Fact]
    public async Task MarkReplacedAsync_NonFailedBatch_IsIgnored()
    {
        var created = await _database.Batches.CreateAsync(
            SqliteTestDatabase.NewBatch("key-1", await _database.AddDocumentAsync("C:/a.cs")), TestSupport.Ct);

        await _database.Batches.MarkReplacedAsync([created.Id], TestSupport.Ct);

        Assert.False((await _database.Batches.GetByIdAsync(created.Id, TestSupport.Ct))!.IsReplaced);
    }

    [Fact]
    public async Task MarkInterruptedAsync_UploadingBatch_BecomesFailedWithNetworkError()
    {
        var failed = await FailedBatchAsync("key-1", "a.cs");
        await _database.Batches.MarkUploadingAsync(failed.Id, TestSupport.Ct);
        var other = await _database.Batches.CreateAsync(
            SqliteTestDatabase.NewBatch("key-2", await _database.AddDocumentAsync("C:/b.cs")), TestSupport.Ct);

        await _database.Batches.MarkInterruptedAsync(TestSupport.Ct);

        var interrupted = await _database.Batches.GetByIdAsync(failed.Id, TestSupport.Ct);
        Assert.Equal(BatchStatus.Failed, interrupted!.Status);
        Assert.Equal("network", interrupted.LastError);
        Assert.Equal(BatchStatus.Draft, (await _database.Batches.GetByIdAsync(other.Id, TestSupport.Ct))!.Status);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownBatch_ReturnsNull()
    {
        Assert.Null(await _database.Batches.GetByIdAsync("nope", TestSupport.Ct));
    }

    private sealed record StoredBatchHandle(string Id, UploadPayload Payload);
}
