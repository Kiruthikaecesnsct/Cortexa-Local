using System.Text.Json;
using System.Text.Json.Serialization;
using Collector.Application.Ports;
using Collector.Application.Remote.Auth;
using Collector.Application.Secrets;
using Collector.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Collector.Infrastructure.Secrets;

internal sealed record OAuthVaultBlob(
    string? Access,
    string? Refresh,
    DateTimeOffset? AccessExpiresAt,
    DateTimeOffset? RefreshExpiresAt);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OAuthVaultBlob))]
internal sealed partial class OAuthVaultJson : JsonSerializerContext;

public sealed class CredentialManagerOAuthVault(ISecretStore secrets, ILogger<CredentialManagerOAuthVault> logger)
    : IOAuthTokenVault
{
    private static readonly Dictionary<RemoteAuthMethod, SecretSlot> Slots = new()
    {
        [RemoteAuthMethod.GitHubDevice] = SecretSlot.GitHubOAuthToken,
        [RemoteAuthMethod.GitHubApp] = SecretSlot.GitHubAppToken,
    };

    private static readonly Dictionary<SourceType, RemoteAuthMethod[]> ProviderMethods = new()
    {
        [SourceType.Github] = [RemoteAuthMethod.GitHubDevice, RemoteAuthMethod.GitHubApp],
    };

    private readonly Lock _lock = new();
    private readonly Dictionary<RemoteAuthMethod, OAuthTokenSet?> _cache = [];
    private int _generation;

    public async Task<OAuthTokenSet?> ReadAsync(RemoteAuthMethod method, CancellationToken cancellationToken)
    {
        if (TryGetCached(method, out var cached, out var generation))
        {
            return cached;
        }

        var loaded = await LoadAsync(method, cancellationToken);
        if (loaded.Cacheable)
        {
            Remember(method, loaded.Tokens, generation);
        }

        return loaded.Tokens;
    }

    public async Task SaveAsync(RemoteAuthMethod method, OAuthTokenSet tokens, CancellationToken cancellationToken)
    {
        var slot = SlotFor(method);
        var blob = new OAuthVaultBlob(tokens.AccessToken, tokens.RefreshToken, tokens.AccessExpiresAt, tokens.RefreshExpiresAt);

        // Cache first: a rotated refresh token must stay usable this session even if the write fails.
        Replace(method, tokens);
        await secrets.WriteAsync(slot, JsonSerializer.Serialize(blob, OAuthVaultJson.Default.OAuthVaultBlob), cancellationToken);
    }

    public async Task DeleteAsync(RemoteAuthMethod method, CancellationToken cancellationToken)
    {
        await secrets.DeleteAsync(SlotFor(method), cancellationToken);
        Replace(method, null);
    }

    public async Task DeleteProviderAsync(SourceType provider, CancellationToken cancellationToken)
    {
        SecretStoreException? firstFailure = null;
        foreach (var method in ProviderMethods.GetValueOrDefault(provider, []))
        {
            try
            {
                await DeleteAsync(method, cancellationToken);
            }
            catch (SecretStoreException ex)
            {
                firstFailure ??= ex;
            }
        }

        // Every slot gets a delete attempt before a failure is reported.
        if (firstFailure is not null)
        {
            throw firstFailure;
        }
    }

    public void DropCache()
    {
        lock (_lock)
        {
            _cache.Clear();
            _generation++;
        }
    }

    private static SecretSlot SlotFor(RemoteAuthMethod method) =>
        Slots.TryGetValue(method, out var slot)
            ? slot
            : throw new ArgumentOutOfRangeException(nameof(method), method, "No stored sign-in for this method.");

    private static OAuthTokenSet? Parse(string raw)
    {
        try
        {
            var blob = JsonSerializer.Deserialize(raw, OAuthVaultJson.Default.OAuthVaultBlob);
            return string.IsNullOrWhiteSpace(blob?.Access)
                ? null
                : new OAuthTokenSet(blob.Access, blob.Refresh, blob.AccessExpiresAt, blob.RefreshExpiresAt);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private bool TryGetCached(RemoteAuthMethod method, out OAuthTokenSet? tokens, out int generation)
    {
        lock (_lock)
        {
            generation = _generation;
            return _cache.TryGetValue(method, out tokens);
        }
    }

    private void Remember(RemoteAuthMethod method, OAuthTokenSet? tokens, int generation)
    {
        lock (_lock)
        {
            if (generation == _generation)
            {
                _cache[method] = tokens;
            }
        }
    }

    private void Replace(RemoteAuthMethod method, OAuthTokenSet? tokens)
    {
        lock (_lock)
        {
            _cache[method] = tokens;
            _generation++;
        }
    }

    private async Task<(OAuthTokenSet? Tokens, bool Cacheable)> LoadAsync(
        RemoteAuthMethod method,
        CancellationToken cancellationToken)
    {
        var slot = SlotFor(method);
        try
        {
            var raw = await secrets.ReadAsync(slot, cancellationToken);
            if (raw is null)
            {
                return (null, true);
            }

            var tokens = Parse(raw);
            if (tokens is null)
            {
                await secrets.DeleteAsync(slot, cancellationToken);
            }

            return (tokens, true);
        }
        catch (SecretStoreException exception)
        {
            logger.LogWarning(exception, "Could not read the stored {Slot} sign-in.", slot);
            return (null, false);
        }
    }
}
