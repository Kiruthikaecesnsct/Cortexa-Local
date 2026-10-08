using Collector.Domain.Enums;

namespace Collector.Domain.Remote;

public sealed record RemoteFileRecord(
    string Id,
    SourceType Provider,
    string RepoUrl,
    string RepoKey,
    string? Branch,
    string? CommitSha,
    string Path,
    string? BlobSha,
    long SizeBytes,
    string LocalPath,
    DateTimeOffset FetchedAt);
