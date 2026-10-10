using Collector.Application.Extraction;
using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Tests.Support;
using Microsoft.Extensions.Time.Testing;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Collector.Tests.Remote;

public class RemoteFetchServiceTests
{
    private const string Branch = "main";
    private const string CommitSha = "commit-1";
    private const int FileCap = 2;

    private readonly RemoteRepository _repository = RemoteData.GitHubRepo();
    private readonly FakeRemoteClient _client = new(SourceType.Github);
    private readonly InMemoryRemoteFileStore _store = new();
    private readonly InMemoryRemoteFileCache _cache = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-07T00:00:00Z"));
    private readonly RemoteFetchOptions _options = new();

    private RemoteFetchService CreateService()
    {
        var options = MsOptions.Create(_options);
        return new RemoteFetchService(
            new FakeRemoteClients(_client),
            new RemoteFileFilter(options),
            options,
            new RemoteFileFetcher(_store, _cache, _time));
    }

    private void SetTree(bool truncated = false, params RemoteTreeEntry[] entries)
    {
        _client.Tree = new RemoteTree(CommitSha, entries, truncated);
        foreach (var entry in entries)
        {
            _client.AddBlob(entry.BlobSha, $"content of {entry.Path}");
        }
    }

    private Task<RemoteFetchResult> FetchAsync(IProgress<RemoteFetchProgress>? progress = null) =>
        CreateService().FetchAsync(new RemoteFetchRequest(_repository, Branch), progress, TestSupport.Ct);

    [Fact]
    public async Task FetchAsync_RepositoryOverLimit_ThrowsRepositoryTooLargeWithoutReadingTree()
    {
        _options.MaxRepositoryBytes = 100;
        var service = CreateService();
        var request = new RemoteFetchRequest(RemoteData.GitHubRepo(101), Branch);

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => service.FetchAsync(request, null, TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.RepositoryTooLarge, exception.Kind);
        Assert.Equal(0, _client.TreeCalls);
    }

    [Fact]
    public async Task FetchAsync_RepositoryAtLimit_IsAllowed()
    {
        const long Limit = 100;
        _options.MaxRepositoryBytes = Limit;
        SetTree(false, RemoteData.Entry("a.md", "s1"));

        var result = await CreateService().FetchAsync(
            new RemoteFetchRequest(RemoteData.GitHubRepo(Limit), Branch),
            null,
            TestSupport.Ct);

        Assert.Equal(1, result.Downloaded);
    }

    [Fact]
    public async Task FetchAsync_FreshFiles_DownloadsAndRecordsEach()
    {
        SetTree(false, RemoteData.Entry("b.md", "s2"), RemoteData.Entry("a.md", "s1"));

        var result = await FetchAsync();

        Assert.Equal(2, result.Downloaded);
        Assert.Equal(0, result.CacheHits);
        Assert.Equal(CommitSha, result.CommitSha);
        Assert.Equal(SourceType.Github, result.Source);
        Assert.Equal(
            [
                $"cache/Github/{_repository.RepoKey}/{Branch}/a.md",
                $"cache/Github/{_repository.RepoKey}/{Branch}/b.md",
            ],
            result.LocalPaths);
        Assert.Equal(["s1", "s2"], _store.All.Select(record => record.BlobSha).Order());
        Assert.All(_store.All, record => Assert.Equal(CommitSha, record.CommitSha));
    }

    [Fact]
    public async Task FetchAsync_SameBlobShaAndFileExists_IsCacheHitWithoutDownload()
    {
        SetTree(false, RemoteData.Entry("a.md", "s1"));
        await _store.UpsertAsync(RemoteData.Record(_repository, Branch, "a.md", "s1"), TestSupport.Ct);
        _cache.MarkExisting(SourceType.Github, _repository.RepoKey, Branch, "a.md");

        var result = await FetchAsync();

        Assert.Equal(1, result.CacheHits);
        Assert.Equal(0, result.Downloaded);
        Assert.Empty(_client.OpenedBlobs);
        Assert.Single(result.LocalPaths);
    }

    [Fact]
    public async Task FetchAsync_SameBlobShaButFileMissing_Redownloads()
    {
        SetTree(false, RemoteData.Entry("a.md", "s1"));
        await _store.UpsertAsync(RemoteData.Record(_repository, Branch, "a.md", "s1"), TestSupport.Ct);

        var result = await FetchAsync();

        Assert.Equal(1, result.Downloaded);
        Assert.Equal(0, result.CacheHits);
        Assert.Equal(["s1"], _client.OpenedBlobs);
    }

    [Fact]
    public async Task FetchAsync_ChangedBlobSha_RedownloadsAndUpdatesRecord()
    {
        SetTree(false, RemoteData.Entry("a.md", "s2"));
        await _store.UpsertAsync(RemoteData.Record(_repository, Branch, "a.md", "s1"), TestSupport.Ct);
        _cache.MarkExisting(SourceType.Github, _repository.RepoKey, Branch, "a.md");

        var result = await FetchAsync();

        Assert.Equal(1, result.Downloaded);
        var stored = await _store.GetAsync(SourceType.Github, _repository.RepoKey, Branch, "a.md", TestSupport.Ct);
        Assert.Equal("s2", stored!.BlobSha);
        Assert.Equal(_time.GetUtcNow(), stored.FetchedAt);
    }

    [Fact]
    public async Task FetchAsync_MoreFilesThanCap_DownloadsCapAndFlagsTruncated()
    {
        _options.MaxFilesPerFetch = FileCap;
        SetTree(false, RemoteData.Entry("a.md", "s1"), RemoteData.Entry("b.md", "s2"), RemoteData.Entry("c.md", "s3"));

        var result = await FetchAsync();

        Assert.Equal(FileCap, result.Downloaded);
        Assert.True(result.Truncated);
    }

    private const string SelectionCommit = "commit-selected";
    private const int LargeSelectionSize = 5000;
    private const int SelectionCap = 3;
    private const int OversizedSelection = 5;

    private Task<RemoteFetchResult> FetchSelectionAsync(RemoteSelection selection)
    {
        foreach (var entry in selection.Entries)
        {
            _client.AddBlob(entry.BlobSha, $"content of {entry.Path}");
        }

        var request = new RemoteFetchRequest(_repository, Branch, selection);
        return CreateService().FetchAsync(request, null, TestSupport.Ct);
    }

    private static RemoteSelection SelectionOf(params RemoteTreeEntry[] entries) =>
        new(SelectionCommit, entries, false);

    [Fact]
    public async Task FetchAsync_WithSelection_DoesNotReadTheTree()
    {
        SetTree(false, RemoteData.Entry("a.md", "s1"));

        await FetchSelectionAsync(SelectionOf(RemoteData.Entry("a.md", "s1")));

        Assert.Equal(0, _client.TreeCalls);
    }

    [Fact]
    public async Task FetchAsync_WithSelection_DownloadsOnlyTheSuppliedEntries()
    {
        SetTree(false, RemoteData.Entry("a.md", "s1"), RemoteData.Entry("b.md", "s2"), RemoteData.Entry("c.md", "s3"));

        var result = await FetchSelectionAsync(SelectionOf(RemoteData.Entry("b.md", "s2")));

        Assert.Equal(["s2"], _client.OpenedBlobs);
        Assert.Equal(1, result.Downloaded);
    }

    [Fact]
    public async Task FetchAsync_WithSelection_UsesTheSelectionCommit()
    {
        var result = await FetchSelectionAsync(SelectionOf(RemoteData.Entry("a.md", "s1")));

        Assert.Equal(SelectionCommit, result.CommitSha);
        Assert.Equal(SelectionCommit, Assert.Single(_store.All).CommitSha);
    }

    [Fact]
    public async Task FetchAsync_WithSelection_ReportsRepoPathNextToTheLocalPath()
    {
        var result = await FetchSelectionAsync(SelectionOf(RemoteData.Entry("docs/guide.md", "s1")));

        var file = Assert.Single(result.Files);
        Assert.Equal("docs/guide.md", file.RepoPath);
        Assert.Equal($"cache/Github/{_repository.RepoKey}/{Branch}/docs/guide.md", file.LocalPath);
    }

    [Fact]
    public async Task FetchAsync_WithSelectionLargerThanMaxFilesPerFetch_IsNotCapped()
    {
        var entries = Enumerable.Range(0, LargeSelectionSize)
            .Select(index => RemoteData.Entry($"docs/file{index}.md", $"sha{index}"))
            .ToArray();

        var result = await FetchSelectionAsync(SelectionOf(entries));

        Assert.True(LargeSelectionSize > _options.MaxFilesPerFetch);
        Assert.Equal(LargeSelectionSize, result.Downloaded);
        Assert.Equal(LargeSelectionSize, result.Files.Count);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task FetchAsync_WithSelectionOverMaxSelectedFiles_CapsAndFlagsTruncated()
    {
        _options.MaxSelectedFiles = SelectionCap;
        var entries = Enumerable.Range(0, OversizedSelection)
            .Select(index => RemoteData.Entry($"file{index}.md", $"sha{index}"))
            .ToArray();

        var result = await FetchSelectionAsync(SelectionOf(entries));

        Assert.Equal(SelectionCap, result.Downloaded);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task FetchAsync_WithSelectionContainingUnsupportedEntry_SkipsItAndCountsIt()
    {
        var result = await FetchSelectionAsync(SelectionOf(RemoteData.Entry("a.md", "s1"), RemoteData.Entry("logo.png", "s2")));

        Assert.Equal(1, result.SkippedByFilter);
        Assert.Equal(["s1"], _client.OpenedBlobs);
    }

    [Fact]
    public async Task FetchAsync_WithTruncatedSelection_FlagsTruncated()
    {
        var selection = new RemoteSelection(SelectionCommit, [RemoteData.Entry("a.md", "s1")], true);

        var result = await FetchSelectionAsync(selection);

        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task FetchAsync_WithSelection_ReportsProgressAgainstTheSelectionSize()
    {
        var progress = new ListProgress<RemoteFetchProgress>();
        var selection = SelectionOf(RemoteData.Entry("a.md", "s1"), RemoteData.Entry("b.md", "s2"));
        foreach (var entry in selection.Entries)
        {
            _client.AddBlob(entry.BlobSha, "content");
        }

        await CreateService().FetchAsync(new RemoteFetchRequest(_repository, Branch, selection), progress, TestSupport.Ct);

        Assert.Equal(new RemoteFetchProgress(RemoteFetchPhase.Completed, 2, 2), progress.Items[^1]);
    }

    [Fact]
    public async Task FetchAsync_FilesWithinCap_IsNotTruncated()
    {
        _options.MaxFilesPerFetch = FileCap;
        SetTree(false, RemoteData.Entry("a.md", "s1"), RemoteData.Entry("b.md", "s2"));

        var result = await FetchAsync();

        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task FetchAsync_TreeTruncatedByProvider_FlagsTruncated()
    {
        SetTree(true, RemoteData.Entry("a.md", "s1"));

        var result = await FetchAsync();

        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task FetchAsync_FilteredEntries_AreCountedAsSkippedAndNotDownloaded()
    {
        SetTree(
            false,
            RemoteData.Entry("a.md", "s1"),
            RemoteData.Entry("node_modules/x.md", "s2"),
            RemoteData.Entry("logo.png", "s3"),
            RemoteData.Entry("huge.md", "s4", FileContentGuard.MaxFileBytes + 1));

        var result = await FetchAsync();

        Assert.Equal(3, result.SkippedByFilter);
        Assert.Equal(["s1"], _client.OpenedBlobs);
    }

    [Fact]
    public async Task FetchAsync_UnsafeLocalPath_IsSkippedWithoutDownload()
    {
        SetTree(false, RemoteData.Entry("a/../evil.md", "s1"), RemoteData.Entry("ok.md", "s2"));

        var result = await FetchAsync();

        Assert.Equal(1, result.SkippedByFilter);
        Assert.Equal(1, result.Downloaded);
        Assert.Equal(["s2"], _client.OpenedBlobs);
    }

    [Fact]
    public async Task FetchAsync_BlobLengthOverLimit_CountsTooLargeAndStoresNothing()
    {
        SetTree(false, RemoteData.Entry("a.md", "s1"));
        _client.LengthOverride = FileContentGuard.MaxFileBytes + 1;

        var result = await FetchAsync();

        Assert.Equal(1, result.TooLarge);
        Assert.Equal(0, result.Downloaded);
        Assert.Empty(result.LocalPaths);
        Assert.Empty(_store.All);
        Assert.Empty(_cache.Written);
    }

    [Fact]
    public async Task FetchAsync_CacheReportsExceededLimit_CountsTooLargeAndStoresNothing()
    {
        SetTree(false, RemoteData.Entry("a.md", "s1"));
        _cache.ExceedLimit = true;

        var result = await FetchAsync();

        Assert.Equal(1, result.TooLarge);
        Assert.Empty(result.LocalPaths);
        Assert.Empty(_store.All);
    }

    [Fact]
    public async Task FetchAsync_Progress_ReportsReadingThenDownloadingThenCompleted()
    {
        SetTree(false, RemoteData.Entry("a.md", "s1"), RemoteData.Entry("b.md", "s2"));
        var progress = new ListProgress<RemoteFetchProgress>();

        await FetchAsync(progress);

        var reports = progress.Items;
        Assert.Equal(new RemoteFetchProgress(RemoteFetchPhase.ReadingTree, 0, 0), reports[0]);
        Assert.Equal(new RemoteFetchProgress(RemoteFetchPhase.Completed, 2, 2), reports[^1]);
        var downloading = reports.Where(report => report.Phase == RemoteFetchPhase.Downloading).ToList();
        Assert.Equal([1, 2], downloading.Select(report => report.Processed).Order());
        Assert.All(downloading, report => Assert.Equal(2, report.Total));
    }

    [Fact]
    public async Task FetchAsync_NoProgressObserver_StillCompletes()
    {
        SetTree(false, RemoteData.Entry("a.md", "s1"));

        var result = await FetchAsync(null);

        Assert.Equal(1, result.Downloaded);
    }

    [Fact]
    public async Task FetchAsync_ZeroParallelismConfigured_StillDownloads()
    {
        _options.MaxParallelDownloads = 0;
        SetTree(false, RemoteData.Entry("a.md", "s1"));

        var result = await FetchAsync();

        Assert.Equal(1, result.Downloaded);
    }

    private RemoteFetchService CreateCompletingService(CompletingRemoteClient client)
    {
        var options = MsOptions.Create(_options);
        return new RemoteFetchService(
            new FakeRemoteClients(client),
            new RemoteFileFilter(options),
            options,
            new RemoteFileFetcher(_store, _cache, _time));
    }

    private static CompletingRemoteClient CompletingClientWith(string path, string sha)
    {
        var client = new CompletingRemoteClient(SourceType.Github);
        client.SetTree(new RemoteTree(CommitSha, [RemoteData.Entry(path, sha)], false));
        client.AddBlob(sha, "content");
        return client;
    }

    [Fact]
    public async Task FetchAsync_ClientSupportsCompletion_CompletesOnceAfterSuccess()
    {
        var client = CompletingClientWith("a.md", "s1");

        await CreateCompletingService(client).FetchAsync(new RemoteFetchRequest(_repository, Branch), null, TestSupport.Ct);

        Assert.Equal([(_repository, Branch)], client.Completions);
    }

    [Fact]
    public async Task FetchAsync_DownloadThrows_StillCompletesAndRethrows()
    {
        var client = CompletingClientWith("a.md", "s1");
        client.OpenError = new RemoteSourceException(RemoteFailureKind.Upstream, SourceType.Github);

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(
            () => CreateCompletingService(client).FetchAsync(new RemoteFetchRequest(_repository, Branch), null, TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.Upstream, exception.Kind);
        Assert.Equal([(_repository, Branch)], client.Completions);
    }

    [Fact]
    public async Task FetchAsync_Canceled_StillCompletes()
    {
        var client = CompletingClientWith("a.md", "s1");
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateCompletingService(client).FetchAsync(new RemoteFetchRequest(_repository, Branch), null, canceled.Token));

        Assert.Equal([(_repository, Branch)], client.Completions);
    }

    [Fact]
    public async Task FetchAsync_RepositoryOverLimit_DoesNotComplete()
    {
        _options.MaxRepositoryBytes = 1;
        var client = CompletingClientWith("a.md", "s1");
        var request = new RemoteFetchRequest(RemoteData.GitHubRepo(2), Branch);

        await Assert.ThrowsAsync<RemoteSourceException>(() => CreateCompletingService(client).FetchAsync(request, null, TestSupport.Ct));

        Assert.Empty(client.Completions);
    }

    [Fact]
    public async Task FetchAsync_AzureSelection_OpensOnlySelectedSupportedObjectsAndNeverReadsTree()
    {
        var azure = new FakeRemoteClient(SourceType.AzureDevops);
        var selection = new RemoteSelection(
            SelectionCommit,
            [
                RemoteData.Entry("b.md", "o2"),
                RemoteData.Entry("a.md", "o1"),
                RemoteData.Entry("logo.png", "o3"),
            ],
            false);
        foreach (var entry in selection.Entries)
        {
            azure.AddBlob(entry.BlobSha, $"content of {entry.Path}");
        }

        var options = MsOptions.Create(_options);
        var service = new RemoteFetchService(
            new FakeRemoteClients(azure),
            new RemoteFileFilter(options),
            options,
            new RemoteFileFetcher(_store, _cache, _time));

        await service.FetchAsync(new RemoteFetchRequest(RemoteData.AzureRepo(), Branch, selection), null, TestSupport.Ct);

        Assert.Equal(["o1", "o2"], azure.OpenedBlobs.Order());
        Assert.Equal(0, azure.TreeCalls);
    }
}
