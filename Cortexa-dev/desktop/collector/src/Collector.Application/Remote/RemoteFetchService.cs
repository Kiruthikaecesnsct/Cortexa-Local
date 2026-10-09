using Collector.Application.Ports;
using Collector.Domain.Remote;
using Microsoft.Extensions.Options;

namespace Collector.Application.Remote;

public sealed class RemoteFetchService
{
    private readonly IRemoteRepositoryClients _clients;
    private readonly RemoteFileFilter _filter;
    private readonly IOptions<RemoteFetchOptions> _options;
    private readonly RemoteFileFetcher _fetcher;

    public RemoteFetchService(
        IRemoteRepositoryClients clients,
        RemoteFileFilter filter,
        IOptions<RemoteFetchOptions> options,
        RemoteFileFetcher fetcher)
    {
        _clients = clients;
        _filter = filter;
        _options = options;
        _fetcher = fetcher;
    }

    public async Task<RemoteFetchResult> FetchAsync(
        RemoteFetchRequest request,
        IProgress<RemoteFetchProgress>? progress,
        CancellationToken cancellationToken)
    {
        var repository = request.Repository;
        EnsureWithinSizeLimit(repository);
        progress?.Report(new RemoteFetchProgress(RemoteFetchPhase.ReadingTree, 0, 0));
        var client = _clients.For(repository.Provider);
        var resolved = await ResolveEntriesAsync(client, request, cancellationToken);
        var context = new RemoteFetchContext(client, repository, request.Branch, resolved.CommitSha);
        var tally = await DownloadAndCompleteAsync(context, resolved.Entries, progress, cancellationToken);
        progress?.Report(new RemoteFetchProgress(RemoteFetchPhase.Completed, resolved.Entries.Count, resolved.Entries.Count));
        return new RemoteFetchResult(
            repository.Provider,
            resolved.CommitSha,
            tally.Files,
            tally.Downloaded,
            tally.CacheHits,
            resolved.SkippedByFilter + tally.Skipped,
            tally.TooLarge,
            resolved.Truncated);
    }

    private void EnsureWithinSizeLimit(RemoteRepository repository)
    {
        if (repository.SizeBytes > _options.Value.MaxRepositoryBytes)
        {
            throw new RemoteSourceException(RemoteFailureKind.RepositoryTooLarge, repository.Provider);
        }
    }

    private Task<ResolvedEntries> ResolveEntriesAsync(
        IRemoteRepositoryClient client,
        RemoteFetchRequest request,
        CancellationToken cancellationToken) =>
        request.Selection is { } selection
            ? Task.FromResult(ResolveSelection(selection))
            : ResolveTreeAsync(client, request, cancellationToken);

    private ResolvedEntries ResolveSelection(RemoteSelection selection)
    {
        var kept = selection.Entries.Where(_filter.Keep).ToList();
        var capped = kept.Take(_options.Value.MaxSelectedFiles).ToList();
        return new ResolvedEntries(
            selection.CommitSha,
            capped,
            selection.Entries.Count - kept.Count,
            selection.TreeTruncated || kept.Count > capped.Count);
    }

    private async Task<ResolvedEntries> ResolveTreeAsync(
        IRemoteRepositoryClient client,
        RemoteFetchRequest request,
        CancellationToken cancellationToken)
    {
        var tree = await client.GetTreeAsync(request.Repository, request.Branch, cancellationToken);
        var kept = tree.Entries.Where(_filter.Keep).ToList();
        var selected = kept.Take(_options.Value.MaxFilesPerFetch).ToList();
        return new ResolvedEntries(
            tree.CommitSha,
            selected,
            tree.Entries.Count - kept.Count,
            tree.Truncated || kept.Count > selected.Count);
    }

    private async Task<RemoteFetchTally> DownloadAndCompleteAsync(
        RemoteFetchContext context,
        IReadOnlyList<RemoteTreeEntry> entries,
        IProgress<RemoteFetchProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            return await DownloadAsync(context, entries, progress, cancellationToken);
        }
        finally
        {
            if (context.Client is IRemoteFetchCompletion completion)
            {
                await completion.CompleteFetchAsync(context.Repository, context.Branch);
            }
        }
    }

    private async Task<RemoteFetchTally> DownloadAsync(
        RemoteFetchContext context,
        IReadOnlyList<RemoteTreeEntry> entries,
        IProgress<RemoteFetchProgress>? progress,
        CancellationToken cancellationToken)
    {
        var cached = await _fetcher.LoadCachedAsync(context, cancellationToken);
        var tally = new RemoteFetchTally();
        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(_options.Value.MaxParallelDownloads, 1),
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(entries, parallel, async (entry, token) =>
        {
            cached.TryGetValue(entry.Path, out var record);
            var outcome = await _fetcher.FetchAsync(context, entry, record, token);
            var processed = tally.Record(entry.Path, outcome);
            progress?.Report(new RemoteFetchProgress(RemoteFetchPhase.Downloading, processed, entries.Count, entry.Path));
        });

        return tally;
    }

    private sealed record ResolvedEntries(
        string CommitSha,
        IReadOnlyList<RemoteTreeEntry> Entries,
        int SkippedByFilter,
        bool Truncated);
}
