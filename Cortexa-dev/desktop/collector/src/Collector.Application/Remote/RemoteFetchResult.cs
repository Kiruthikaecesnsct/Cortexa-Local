using Collector.Domain.Enums;

namespace Collector.Application.Remote;

public sealed record FetchedFile(string LocalPath, string RepoPath);

public sealed record RemoteFetchResult(
    SourceType Source,
    string CommitSha,
    IReadOnlyList<FetchedFile> Files,
    int Downloaded,
    int CacheHits,
    int SkippedByFilter,
    int TooLarge,
    bool Truncated)
{
    public IReadOnlyList<string> LocalPaths => [.. Files.Select(file => file.LocalPath)];
}
