namespace Collector.Application.Remote;

public enum RemoteFailureKind
{
    MissingToken,
    Auth,
    AccessDenied,
    SsoRequired,
    NotFound,
    EmptyRepository,
    RateLimited,
    RepositoryTooLarge,
    Upstream,
}
