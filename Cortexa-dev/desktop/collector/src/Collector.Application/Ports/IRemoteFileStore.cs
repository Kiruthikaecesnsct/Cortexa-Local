using Collector.Domain.Enums;
using Collector.Domain.Remote;

namespace Collector.Application.Ports;

public interface IRemoteFileStore
{
    Task UpsertAsync(RemoteFileRecord record, CancellationToken cancellationToken);

    Task<RemoteFileRecord?> GetAsync(
        SourceType provider,
        string repoKey,
        string? branch,
        string path,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RemoteFileRecord>> ListAsync(
        SourceType provider,
        string repoKey,
        string? branch,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        SourceType provider,
        string repoKey,
        string? branch,
        string path,
        CancellationToken cancellationToken);
}
