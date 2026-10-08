using Collector.Domain.Enums;

namespace Collector.Domain.Remote;

public sealed record RemoteRepository(
    SourceType Provider,
    string Owner,
    string? Project,
    string Name,
    string FullName,
    string DefaultBranch,
    string WebUrl,
    long SizeBytes,
    bool IsPrivate)
{
    public string RepoKey => Project is null ? $"{Owner}/{Name}" : $"{Owner}/{Project}/{Name}";
}
