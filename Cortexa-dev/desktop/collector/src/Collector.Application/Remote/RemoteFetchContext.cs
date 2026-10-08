using Collector.Application.Ports;
using Collector.Domain.Remote;

namespace Collector.Application.Remote;

public sealed record RemoteFetchContext(
    IRemoteRepositoryClient Client,
    RemoteRepository Repository,
    string Branch,
    string CommitSha);
