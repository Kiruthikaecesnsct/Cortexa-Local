using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Domain.Remote;

namespace Collector.Application.Ports;

public interface IRemoteRepositoryClient
{
    SourceType Provider { get; }

    Task<IReadOnlyList<RemoteRepository>> ListRepositoriesAsync(string? scope, CancellationToken cancellationToken);

    Task<IReadOnlyList<RemoteBranch>> ListBranchesAsync(RemoteRepository repository, CancellationToken cancellationToken);

    Task<RemoteTree> GetTreeAsync(RemoteRepository repository, string branch, CancellationToken cancellationToken);

    Task<RemoteBlob> OpenBlobAsync(RemoteRepository repository, string blobSha, CancellationToken cancellationToken);
}
