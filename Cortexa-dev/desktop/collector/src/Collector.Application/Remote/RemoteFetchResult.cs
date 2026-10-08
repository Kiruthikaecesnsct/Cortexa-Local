using Collector.Domain.Enums;

namespace Collector.Application.Remote;

public sealed record RemoteFetchResult(
    SourceType Source,
    string CommitSha,
    IReadOnlyList<string> LocalPaths,
    int Downloaded,
    int CacheHits,
    int SkippedByFilter,
    int TooLarge,
    bool Truncated);
