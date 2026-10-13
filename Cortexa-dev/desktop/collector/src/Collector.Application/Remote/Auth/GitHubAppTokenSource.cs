using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Domain.Enums;

namespace Collector.Application.Remote.Auth;

public sealed class GitHubAppTokenSource(
    IOAuthTokenVault vault,
    IDeviceAuthorizationClient client,
    IRemoteAuthState state,
    OAuthRefreshPolicy policy,
    TimeProvider time) : IRemoteTokenSource
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public SourceType Provider => SourceType.Github;

    public RemoteAuthMethod Method => RemoteAuthMethod.GitHubApp;

    public async Task<RemoteAccessToken?> GetAsync(CancellationToken cancellationToken)
    {
        var tokens = await vault.ReadAsync(Method, cancellationToken);
        if (tokens is null)
        {
            return null;
        }

        return tokens.NeedsRefresh(time.GetUtcNow(), policy.Skew)
            ? await RefreshAsync(cancellationToken)
            : ToToken(tokens);
    }

    private async Task<RemoteAccessToken?> RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var tokens = await vault.ReadAsync(Method, cancellationToken);
            if (tokens is null)
            {
                return null;
            }

            return tokens.NeedsRefresh(time.GetUtcNow(), policy.Skew)
                ? await ExchangeAsync(tokens, cancellationToken)
                : ToToken(tokens);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<RemoteAccessToken> ExchangeAsync(OAuthTokenSet tokens, CancellationToken cancellationToken)
    {
        if (!tokens.CanRefresh(time.GetUtcNow()))
        {
            throw await ExpireAsync();
        }

        var result = await client.RefreshAsync(Method, tokens.RefreshToken!, cancellationToken);
        return result switch
        {
            RefreshResult.Refreshed refreshed => await StoreAsync(refreshed.Tokens),
            RefreshResult.Rejected => throw await ExpireAsync(),
            _ => StillValidOrThrow(tokens),
        };
    }

    private async Task<RemoteAccessToken> StoreAsync(OAuthTokenSet rotated)
    {
        try
        {
            await vault.SaveAsync(Method, rotated, CancellationToken.None);
        }
        catch (SecretStoreException)
        {
            // The old refresh token is already spent, so keep this session going on the new one.
        }

        return ToToken(rotated);
    }

    private RemoteAccessToken StillValidOrThrow(OAuthTokenSet tokens) =>
        tokens.AccessExpiresAt is { } expiry && time.GetUtcNow() < expiry
            ? ToToken(tokens)
            : throw new RemoteSourceException(RemoteFailureKind.Upstream, Provider);

    private async Task<RemoteSourceException> ExpireAsync()
    {
        await vault.DeleteAsync(Method, CancellationToken.None);
        state.Reset(Provider);
        return new RemoteSourceException(RemoteFailureKind.Auth, Provider);
    }

    private static RemoteAccessToken ToToken(OAuthTokenSet tokens) =>
        new(tokens.AccessToken, RemoteTokenKind.OAuth);
}
