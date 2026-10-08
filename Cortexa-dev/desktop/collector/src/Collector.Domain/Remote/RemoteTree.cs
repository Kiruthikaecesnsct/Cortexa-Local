namespace Collector.Domain.Remote;

public sealed record RemoteTreeEntry(string Path, string BlobSha, long? SizeBytes);

public sealed record RemoteTree(string CommitSha, IReadOnlyList<RemoteTreeEntry> Entries, bool Truncated);
