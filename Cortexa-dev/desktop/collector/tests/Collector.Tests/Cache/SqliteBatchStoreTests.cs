using Collector.Domain.Enums;
using Collector.Tests.Support;

namespace Collector.Tests.Cache;

public sealed class SqliteBatchStoreTests : IAsyncLifetime
{
    private SqliteTestDatabase _database = null!;

    public async ValueTask InitializeAsync() => _database = await SqliteTestDatabase.CreateAsync();

    public ValueTask DisposeAsync()
    {
        _database.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<string[]> TwoDocumentsAsync() =>
        [await _database.AddDocumentAsync("C:/a.cs"), await _database.AddDocumentAsync("C:/b.cs")];

    [Fact]
    public async Task CreateAsync_NewBatch_IsDraftAndFindableByItsDocuments()
    {
        var documents = await TwoDocumentsAsync();

        var created = await _database.Batches.CreateAsync(SqliteTestDatabase.NewBatch("key-1", documents), TestSupport.Ct);
        var found = await _database.Batches.FindByDocumentsAsync(documents, TestSupport.Ct);

        Assert.Equal(BatchStatus.Draft, created.Status);
        Assert.Equal(created.Id, found!.Id);
        Assert.Equal("key-1", found.IdempotencyKey);
        Assert.Equal(BatchStatus.Draft, found.Status);
        Assert.Equal(documents.Order(), found.DocumentIds);
    }

    [Fact]
    public async Task MarkUploadingAsync_Batch_StatusBecomesUploading()
    {
        var documents = await TwoDocumentsAsync();
        var created = await _database.Batches.CreateAsync(SqliteTestDatabase.NewBatch("key-1", documents), TestSupport.Ct);

        await _database.Batches.MarkUploadingAsync(created.Id, TestSupport.Ct);

        var found = await _database.Batches.FindByDocumentsAsync(documents, TestSupport.Ct);
        Assert.Equal(BatchStatus.Uploading, found!.Status);
    }

    [Fact]
    public async Task MarkUploadedAsync_Batch_StoresServerBatchId()
    {
        var documents = await TwoDocumentsAsync();
        var created = await _database.Batches.CreateAsync(SqliteTestDatabase.NewBatch("key-1", documents), TestSupport.Ct);

        await _database.Batches.MarkUploadedAsync(created.Id, "server-42", TestSupport.Ct);

        var found = await _database.Batches.FindByDocumentsAsync(documents, TestSupport.Ct);
        Assert.Equal(BatchStatus.Uploaded, found!.Status);
        Assert.Equal("server-42", found.ServerBatchId);
        Assert.Null(found.LastError);
    }

    [Fact]
    public async Task MarkFailedAsync_Batch_StoresError()
    {
        var documents = await TwoDocumentsAsync();
        var created = await _database.Batches.CreateAsync(SqliteTestDatabase.NewBatch("key-1", documents), TestSupport.Ct);

        await _database.Batches.MarkFailedAsync(created.Id, "rejected:validation_failed", TestSupport.Ct);

        var found = await _database.Batches.FindByDocumentsAsync(documents, TestSupport.Ct);
        Assert.Equal(BatchStatus.Failed, found!.Status);
        Assert.Equal("rejected:validation_failed", found.LastError);
    }

    [Fact]
    public async Task MarkUploadedAsync_AfterFailure_ClearsLastError()
    {
        var documents = await TwoDocumentsAsync();
        var created = await _database.Batches.CreateAsync(SqliteTestDatabase.NewBatch("key-1", documents), TestSupport.Ct);
        await _database.Batches.MarkFailedAsync(created.Id, "network", TestSupport.Ct);

        await _database.Batches.MarkUploadedAsync(created.Id, "server-1", TestSupport.Ct);

        var found = await _database.Batches.FindByDocumentsAsync(documents, TestSupport.Ct);
        Assert.Null(found!.LastError);
    }

    [Fact]
    public async Task FindByDocumentsAsync_SameSetInDifferentOrder_Matches()
    {
        var documents = await TwoDocumentsAsync();
        await _database.Batches.CreateAsync(SqliteTestDatabase.NewBatch("key-1", documents), TestSupport.Ct);

        var found = await _database.Batches.FindByDocumentsAsync([documents[1], documents[0]], TestSupport.Ct);

        Assert.NotNull(found);
    }

    [Fact]
    public async Task FindByDocumentsAsync_SubsetOfBatchDocuments_ReturnsNull()
    {
        var documents = await TwoDocumentsAsync();
        await _database.Batches.CreateAsync(SqliteTestDatabase.NewBatch("key-1", documents), TestSupport.Ct);

        var found = await _database.Batches.FindByDocumentsAsync([documents[0]], TestSupport.Ct);

        Assert.Null(found);
    }

    [Fact]
    public async Task FindByDocumentsAsync_SupersetOfBatchDocuments_ReturnsNull()
    {
        var documents = await TwoDocumentsAsync();
        var third = await _database.AddDocumentAsync("C:/c.cs");
        await _database.Batches.CreateAsync(SqliteTestDatabase.NewBatch("key-1", documents), TestSupport.Ct);

        var found = await _database.Batches.FindByDocumentsAsync([.. documents, third], TestSupport.Ct);

        Assert.Null(found);
    }

    [Fact]
    public async Task FindByDocumentsAsync_EmptySet_ReturnsNull()
    {
        var found = await _database.Batches.FindByDocumentsAsync([], TestSupport.Ct);

        Assert.Null(found);
    }

    [Fact]
    public async Task FindByDocumentsAsync_TwoBatchesForSameSet_ReturnsNewest()
    {
        var documents = await TwoDocumentsAsync();
        await _database.Batches.CreateAsync(SqliteTestDatabase.NewBatch("older", documents), TestSupport.Ct);
        _database.Time.Advance(TimeSpan.FromMinutes(5));
        await _database.Batches.CreateAsync(SqliteTestDatabase.NewBatch("newer", documents), TestSupport.Ct);

        var found = await _database.Batches.FindByDocumentsAsync(documents, TestSupport.Ct);

        Assert.Equal("newer", found!.IdempotencyKey);
    }
}
