using Collector.Application.Extraction;
using Collector.Application.Ports;
using Collector.Domain.Remote;

namespace Collector.Application.Remote;

public sealed class RemoteFileFetcher(IRemoteFileStore store, IRemoteFileCache cache, TimeProvider timeProvider)
{
    public async Task<IReadOnlyDictionary<string, RemoteFileRecord>> LoadCachedAsync(
        RemoteFetchContext context,
        CancellationToken cancellationToken)
    {
        var repository = context.Repository;
        var records = await store.ListAsync(repository.Provider, repository.RepoKey, context.Branch, cancellationToken);
        return records.ToDictionary(record => record.Path, StringComparer.Ordinal);
    }

    public async Task<RemoteFileOutcome> FetchAsync(
        RemoteFetchContext context,
        RemoteTreeEntry entry,
        RemoteFileRecord? cached,
        CancellationToken cancellationToken)
    {
        var repository = context.Repository;
        var localPath = cache.ResolvePath(repository.Provider, repository.RepoKey, context.Branch, entry.Path);
        if (localPath is null)
        {
            return RemoteFileOutcome.Skipped;
        }

        if (cached?.BlobSha == entry.BlobSha && cache.Exists(localPath))
        {
            return RemoteFileOutcome.CacheHit(localPath);
        }

        return await DownloadAsync(context, entry, localPath, cancellationToken);
    }

    private async Task<RemoteFileOutcome> DownloadAsync(
        RemoteFetchContext context,
        RemoteTreeEntry entry,
        string localPath,
        CancellationToken cancellationToken)
    {
        using var blob = await context.Client.OpenBlobAsync(context.Repository, entry.BlobSha, cancellationToken);
        if (blob.Length > FileContentGuard.MaxFileBytes)
        {
            return RemoteFileOutcome.TooLarge;
        }

        var written = await cache.WriteAsync(localPath, blob.Content, cancellationToken);
        if (written.ExceededLimit)
        {
            return RemoteFileOutcome.TooLarge;
        }

        await store.UpsertAsync(CreateRecord(context, entry, localPath, written.SizeBytes), cancellationToken);
        return RemoteFileOutcome.Downloaded(localPath);
    }

    private RemoteFileRecord CreateRecord(
        RemoteFetchContext context,
        RemoteTreeEntry entry,
        string localPath,
        long sizeBytes) => new(
            Guid.NewGuid().ToString("n"),
            context.Repository.Provider,
            context.Repository.WebUrl,
            context.Repository.RepoKey,
            context.Branch,
            context.CommitSha,
            entry.Path,
            entry.BlobSha,
            sizeBytes,
            localPath,
            timeProvider.GetUtcNow());
}
