using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Infrastructure.Cache;
using Collector.Tests.Support;

namespace Collector.Tests.Cache;

public sealed class SqliteRemoteFileStoreTests : IAsyncLifetime
{
    private const string RepoKey = "octo/hello";
    private const string Branch = "main";
    private const string FilePath = "docs/a.md";

    private SqliteTestDatabase _database = null!;
    private SqliteRemoteFileStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        _database = await SqliteTestDatabase.CreateAsync();
        _store = new SqliteRemoteFileStore(_database.Connections);
    }

    public ValueTask DisposeAsync()
    {
        _database.Dispose();
        return ValueTask.CompletedTask;
    }

    private static RemoteFileRecord Record(
        string path = FilePath,
        string? branch = Branch,
        string blobSha = "sha-1",
        string repoKey = RepoKey,
        SourceType provider = SourceType.Github,
        long size = 10) => new(
            Guid.NewGuid().ToString("n"),
            provider,
            "https://github.com/octo/hello",
            repoKey,
            branch,
            "commit-1",
            path,
            blobSha,
            size,
            $"cache/{path}",
            DateTimeOffset.Parse("2026-10-07T00:00:00Z"));

    private Task<IReadOnlyList<RemoteFileRecord>> ListAsync(string? branch = Branch, string repoKey = RepoKey) =>
        _store.ListAsync(SourceType.Github, repoKey, branch, TestSupport.Ct);

    [Fact]
    public async Task GetAsync_AfterUpsert_ReturnsStoredRecord()
    {
        var record = Record();
        await _store.UpsertAsync(record, TestSupport.Ct);

        var found = await _store.GetAsync(SourceType.Github, RepoKey, Branch, FilePath, TestSupport.Ct);

        Assert.Equal(record, found);
    }

    [Fact]
    public async Task GetAsync_UnknownPath_ReturnsNull()
    {
        var found = await _store.GetAsync(SourceType.Github, RepoKey, Branch, "nope.md", TestSupport.Ct);

        Assert.Null(found);
    }

    [Fact]
    public async Task UpsertAsync_SameKey_UpdatesExistingRowWithoutDuplicate()
    {
        await _store.UpsertAsync(Record(blobSha: "sha-1", size: 10), TestSupport.Ct);

        await _store.UpsertAsync(Record(blobSha: "sha-2", size: 99), TestSupport.Ct);

        var rows = await ListAsync();
        var row = Assert.Single(rows);
        Assert.Equal("sha-2", row.BlobSha);
        Assert.Equal(99, row.SizeBytes);
    }

    [Fact]
    public async Task UpsertAsync_SameKeyWithNullBranch_UpdatesExistingRowWithoutDuplicate()
    {
        await _store.UpsertAsync(Record(branch: null, blobSha: "sha-1"), TestSupport.Ct);

        await _store.UpsertAsync(Record(branch: null, blobSha: "sha-2"), TestSupport.Ct);

        var row = Assert.Single(await ListAsync(branch: null));
        Assert.Equal("sha-2", row.BlobSha);
        Assert.Null(row.Branch);
    }

    [Fact]
    public async Task UpsertAsync_NullAndNamedBranch_AreSeparateRows()
    {
        await _store.UpsertAsync(Record(branch: null), TestSupport.Ct);
        await _store.UpsertAsync(Record(branch: Branch), TestSupport.Ct);

        Assert.Single(await ListAsync(branch: null));
        Assert.Single(await ListAsync(branch: Branch));
    }

    [Fact]
    public async Task UpsertAsync_SamePathInDifferentRepositories_AreSeparateRows()
    {
        await _store.UpsertAsync(Record(repoKey: "octo/one"), TestSupport.Ct);
        await _store.UpsertAsync(Record(repoKey: "octo/two"), TestSupport.Ct);

        Assert.Single(await ListAsync(repoKey: "octo/one"));
        Assert.Single(await ListAsync(repoKey: "octo/two"));
    }

    [Fact]
    public async Task UpsertAsync_SameKeyDifferentProvider_AreSeparateRows()
    {
        await _store.UpsertAsync(Record(provider: SourceType.Github), TestSupport.Ct);
        await _store.UpsertAsync(Record(provider: SourceType.AzureDevops), TestSupport.Ct);

        var github = await _store.ListAsync(SourceType.Github, RepoKey, Branch, TestSupport.Ct);
        var azure = await _store.ListAsync(SourceType.AzureDevops, RepoKey, Branch, TestSupport.Ct);

        Assert.Single(github);
        Assert.Single(azure);
    }

    [Fact]
    public async Task ListAsync_MultipleFiles_ReturnsOnlyRequestedBranchOrderedByPath()
    {
        await _store.UpsertAsync(Record(path: "b.md"), TestSupport.Ct);
        await _store.UpsertAsync(Record(path: "a.md"), TestSupport.Ct);
        await _store.UpsertAsync(Record(path: "c.md", branch: "dev"), TestSupport.Ct);

        var rows = await ListAsync();

        Assert.Equal(["a.md", "b.md"], rows.Select(row => row.Path));
    }

    [Fact]
    public async Task ListAsync_NothingStored_ReturnsEmpty()
    {
        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task DeleteAsync_ExistingRow_RemovesOnlyThatRow()
    {
        await _store.UpsertAsync(Record(path: "a.md"), TestSupport.Ct);
        await _store.UpsertAsync(Record(path: "b.md"), TestSupport.Ct);

        await _store.DeleteAsync(SourceType.Github, RepoKey, Branch, "a.md", TestSupport.Ct);

        Assert.Equal(["b.md"], (await ListAsync()).Select(row => row.Path));
    }

    [Fact]
    public async Task DeleteAsync_NullBranchRow_RemovesIt()
    {
        await _store.UpsertAsync(Record(branch: null), TestSupport.Ct);

        await _store.DeleteAsync(SourceType.Github, RepoKey, null, FilePath, TestSupport.Ct);

        Assert.Empty(await ListAsync(branch: null));
    }

    [Fact]
    public async Task DeleteAsync_UnknownRow_DoesNotThrow()
    {
        await _store.DeleteAsync(SourceType.Github, RepoKey, Branch, "nope.md", TestSupport.Ct);

        Assert.Empty(await ListAsync());
    }
}
