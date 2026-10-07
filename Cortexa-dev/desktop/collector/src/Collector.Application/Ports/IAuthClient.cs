using Collector.Application.Auth;

namespace Collector.Application.Ports;

public interface IAuthClient
{
    Task<AuthCallResult> LoginAsync(string email, string password, CancellationToken cancellationToken);

    Task<AuthCallResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
}
