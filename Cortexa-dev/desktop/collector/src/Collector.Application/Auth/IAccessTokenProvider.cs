namespace Collector.Application.Auth;

public interface IAccessTokenProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken);

    Task<string?> RefreshAfterUnauthorizedAsync(string rejectedAccessToken, CancellationToken cancellationToken);

    Task<bool> RefreshAsync(CancellationToken cancellationToken);

    Task MarkExpiredAsync(CancellationToken cancellationToken);
}
