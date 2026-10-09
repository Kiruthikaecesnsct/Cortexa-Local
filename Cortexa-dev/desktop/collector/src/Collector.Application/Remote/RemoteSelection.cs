using Collector.Domain.Remote;

namespace Collector.Application.Remote;

public sealed record RemoteSelection(string CommitSha, IReadOnlyList<RemoteTreeEntry> Entries, bool TreeTruncated);
