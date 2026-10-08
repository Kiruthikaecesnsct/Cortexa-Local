using Collector.Domain.Remote;

namespace Collector.Application.Remote;

public sealed record RemoteFetchRequest(RemoteRepository Repository, string Branch);
