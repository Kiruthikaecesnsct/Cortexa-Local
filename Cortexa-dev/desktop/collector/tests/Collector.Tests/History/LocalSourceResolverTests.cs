using Collector.Application.History;
using Collector.Domain.Enums;
using Collector.Domain.Upload;
using Collector.Tests.Extraction;
using Collector.Tests.Support;

namespace Collector.Tests.History;

public sealed class LocalSourceResolverTests
{
    private const string ServerBatchId = "server-batch-1";
    private const string LocalPath = "C:/work/paper.pdf";

    private readonly InMemoryBatchStore _batches = new();
    private readonly InMemoryDocumentStore _documents = new();

    private LocalSourceResolver Resolver() => new(_batches, _documents);

    private static string ServerDocumentId(string localId) =>
        DeterministicIds.DocumentId(ServerBatchId, Guid.Parse(localId));

    private async Task<string> AddDocumentAsync(string path, SourceType type = SourceType.Local)
    {
        var document = await _documents.UpsertAsync(
            type,
            SourceKind.Paper,
            path,
            Path.GetFileName(path),
            "hash",
            1,
            TestSupport.Ct);
        return document.Id;
    }

    private async Task UploadBatchAsync(params string[] documentIds)
    {
        var created = await _batches.CreateAsync(SqliteTestDatabase.NewBatch("key-1", documentIds), TestSupport.Ct);
        await _batches.MarkUploadedAsync(created.Id, ServerBatchId, TestSupport.Ct);
    }

    [Fact]
    public async Task ResolveAsync_LocalDocument_ReturnsPathAndKind()
    {
        var localId = await AddDocumentAsync(LocalPath);
        await UploadBatchAsync(localId);

        var target = await Resolver().ResolveAsync(ServerBatchId, ServerDocumentId(localId), TestSupport.Ct);

        Assert.Equal(new LocalSourceTarget(LocalPath, SourceKind.Paper), target);
    }

    [Fact]
    public async Task ResolveAsync_NonLocalDocument_ReturnsNull()
    {
        var localId = await AddDocumentAsync("https://github.com/org/repo/a.cs", SourceType.Github);
        await UploadBatchAsync(localId);

        var target = await Resolver().ResolveAsync(ServerBatchId, ServerDocumentId(localId), TestSupport.Ct);

        Assert.Null(target);
    }

    [Fact]
    public async Task ResolveAsync_UnknownServerDocumentId_ReturnsNull()
    {
        var localId = await AddDocumentAsync(LocalPath);
        await UploadBatchAsync(localId);

        var target = await Resolver().ResolveAsync(ServerBatchId, "unknown-document", TestSupport.Ct);

        Assert.Null(target);
    }

    [Fact]
    public async Task ResolveAsync_BatchNotUploadedFromThisMachine_ReturnsNull()
    {
        var target = await Resolver().ResolveAsync("foreign-batch", Guid.NewGuid().ToString(), TestSupport.Ct);

        Assert.Null(target);
    }

    [Fact]
    public async Task ResolveAsync_NonGuidLocalId_IsSkippedWithoutFailing()
    {
        var localId = await AddDocumentAsync(LocalPath);
        await UploadBatchAsync("not-a-guid", localId);

        var target = await Resolver().ResolveAsync(ServerBatchId, ServerDocumentId(localId), TestSupport.Ct);

        Assert.NotNull(target);
    }

    [Fact]
    public async Task ResolveAsync_LocalIdMissingFromDocumentStore_IsSkipped()
    {
        var present = await AddDocumentAsync(LocalPath);
        var missing = Guid.NewGuid().ToString("n");
        await UploadBatchAsync(missing, present);

        var resolver = Resolver();

        Assert.Null(await resolver.ResolveAsync(ServerBatchId, ServerDocumentId(missing), TestSupport.Ct));
        Assert.NotNull(await resolver.ResolveAsync(ServerBatchId, ServerDocumentId(present), TestSupport.Ct));
    }

    [Fact]
    public async Task ResolveAsync_RepeatedCalls_BuildTheMapOnce()
    {
        var localId = await AddDocumentAsync(LocalPath);
        await UploadBatchAsync(localId);
        var resolver = Resolver();

        await resolver.ResolveAsync(ServerBatchId, ServerDocumentId(localId), TestSupport.Ct);
        await resolver.ResolveAsync(ServerBatchId, ServerDocumentId(localId), TestSupport.Ct);

        Assert.Equal(1, _batches.FindByServerBatchIdCalls);
    }

    [Fact]
    public async Task Invalidate_AfterResolve_RebuildsTheMapOnNextCall()
    {
        var localId = await AddDocumentAsync(LocalPath);
        await UploadBatchAsync(localId);
        var resolver = Resolver();
        await resolver.ResolveAsync(ServerBatchId, ServerDocumentId(localId), TestSupport.Ct);

        resolver.Invalidate(ServerBatchId);
        await resolver.ResolveAsync(ServerBatchId, ServerDocumentId(localId), TestSupport.Ct);

        Assert.Equal(2, _batches.FindByServerBatchIdCalls);
    }

    [Fact]
    public async Task ResolveAsync_DifferentServerBatches_AreCachedSeparately()
    {
        var resolver = Resolver();

        await resolver.ResolveAsync("batch-a", Guid.NewGuid().ToString(), TestSupport.Ct);
        await resolver.ResolveAsync("batch-b", Guid.NewGuid().ToString(), TestSupport.Ct);

        Assert.Equal(2, _batches.FindByServerBatchIdCalls);
    }
}
