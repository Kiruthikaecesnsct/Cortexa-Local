using System.Collections.Concurrent;

namespace Collector.Application.Remote;

internal sealed class RemoteFetchTally
{
    private readonly ConcurrentBag<FetchedFile> _files = [];
    private int _downloaded;
    private int _cacheHits;
    private int _tooLarge;
    private int _skipped;
    private int _processed;

    public int Downloaded => _downloaded;

    public int CacheHits => _cacheHits;

    public int TooLarge => _tooLarge;

    public int Skipped => _skipped;

    public IReadOnlyList<FetchedFile> Files => [.. _files.OrderBy(file => file.LocalPath, StringComparer.OrdinalIgnoreCase)];

    public int Record(string repoPath, RemoteFileOutcome outcome)
    {
        switch (outcome.Kind)
        {
            case RemoteFileOutcomeKind.Downloaded:
                Interlocked.Increment(ref _downloaded);
                break;
            case RemoteFileOutcomeKind.CacheHit:
                Interlocked.Increment(ref _cacheHits);
                break;
            case RemoteFileOutcomeKind.TooLarge:
                Interlocked.Increment(ref _tooLarge);
                break;
            default:
                Interlocked.Increment(ref _skipped);
                break;
        }

        if (outcome.LocalPath is not null)
        {
            _files.Add(new FetchedFile(outcome.LocalPath, repoPath));
        }

        return Interlocked.Increment(ref _processed);
    }
}
