using Collector.Application.Ports;
using Collector.Infrastructure.Options;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Ai;

public sealed class VertexTokenSource(
    IVertexCredentialStore credentialStore,
    TimeProvider timeProvider,
    IOptions<VertexProviderOptions> options) : IVertexTokenSource
{
    public const string MissingCredentialMessage = "Add your Vertex service account JSON in Settings.";

    private static readonly string[] Scopes = ["https://www.googleapis.com/auth/cloud-platform"];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CachedToken? _cached;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is { } cached && !IsExpiring(cached.ExpiresAtUtc))
            {
                return cached.AccessToken;
            }

            var fresh = await ExchangeAsync(cancellationToken).ConfigureAwait(false);
            _cached = fresh;
            return fresh.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsExpiring(DateTimeOffset expiresAtUtc) =>
        timeProvider.GetUtcNow().AddSeconds(options.Value.TokenRefreshSkewSeconds) >= expiresAtUtc;

    private async Task<CachedToken> ExchangeAsync(CancellationToken cancellationToken)
    {
        var serviceAccountJson = await credentialStore.GetServiceAccountJsonAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(serviceAccountJson))
        {
            throw new AiProviderException(AiFailureKind.MissingApiKey, MissingCredentialMessage);
        }

        return await RequestTokenAsync(serviceAccountJson, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CachedToken> RequestTokenAsync(string serviceAccountJson, CancellationToken cancellationToken)
    {
        try
        {
            var credential = BuildServiceAccountCredential(serviceAccountJson);
            var requested = await credential.RequestAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            if (!requested || credential.Token?.AccessToken is not { } accessToken)
            {
                throw new AiProviderException(AiFailureKind.Permanent, "Vertex did not return an access token.");
            }

            var expiresAt = timeProvider.GetUtcNow().AddSeconds(credential.Token.ExpiresInSeconds ?? 0);
            return new CachedToken(accessToken, expiresAt);
        }
        catch (AiProviderException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AiProviderException(
                AiFailureKind.Permanent,
                "Vertex credentials could not be exchanged for an access token.",
                exception);
        }
    }

    private static ServiceAccountCredential BuildServiceAccountCredential(string serviceAccountJson)
    {
        var parsed = CredentialFactory.FromJson<ServiceAccountCredential>(serviceAccountJson);
        var scoped = GoogleCredential.FromServiceAccountCredential(parsed).CreateScoped(Scopes);
        return scoped.UnderlyingCredential as ServiceAccountCredential
            ?? throw new AiProviderException(AiFailureKind.Permanent, "Vertex service account JSON is not a valid service account key.");
    }

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAtUtc);
}
