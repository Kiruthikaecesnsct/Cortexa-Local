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
        var tree = await client.GetTreeAsync(repository, request.Branch, cancellationToken);
        var context = new RemoteFetchContext(client, repository, request.Branch, tree.CommitSha);
        var kept = tree.Entries.Where(_filter.Keep).ToList();
        var selected = kept.Take(_options.Value.MaxFilesPerFetch).ToList();
        var tally = await DownloadAndCompleteAsync(context, selected, progress, cancellationToken);
        progress?.Report(new RemoteFetchProgress(RemoteFetchPhase.Completed, selected.Count, selected.Count));
        var skipped = tree.Entries.Count - kept.Count + tally.Skipped;
        return new RemoteFetchResult(
            repository.Provider,
            tree.CommitSha,
            tally.Paths,
            tally.Downloaded,
            tally.CacheHits,
            skipped,
            tally.TooLarge,
            tree.Truncated || kept.Count > selected.Count);
    }

    private void EnsureWithinSizeLimit(RemoteRepository repository)
    {
        if (repository.SizeBytes > _options.Value.MaxRepositoryBytes)
        {
            throw new RemoteSourceException(RemoteFailureKind.RepositoryTooLarge, repository.Provider);
        }
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
            var processed = tally.Record(outcome);
            progress?.Report(new RemoteFetchProgress(RemoteFetchPhase.Downloading, processed, entries.Count));
        });

        return tally;
    }
}
