using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Cortexa.ModelRouter.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Cortexa.ModelRouter.Infrastructure.Security;

public sealed class KeyVaultProviderKeyResolver : IProviderKeyResolver
{
    private readonly SecretClient _client;
    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<string, (string Value, DateTimeOffset ExpiresAt)> _cache = new();

    public KeyVaultProviderKeyResolver(IConfiguration configuration, IOptions<KeyResolverSettings> options)
    {
        var vaultUri = configuration["KeyVault:Uri"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(vaultUri))
            throw new InvalidOperationException("KeyVault:Uri must not be empty.");
        _client = new SecretClient(new Uri(vaultUri), new DefaultAzureCredential());
        _ttl = TimeSpan.FromSeconds(options.Value.SecretCacheTtlSeconds);
    }

    public async Task<string> ResolveAsync(string secretName, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(secretName, out var entry) && DateTimeOffset.UtcNow < entry.ExpiresAt)
            return entry.Value;

        var secret = await _client.GetSecretAsync(secretName, cancellationToken: ct);
        var value = secret.Value.Value;
        _cache[secretName] = (value, DateTimeOffset.UtcNow.Add(_ttl));
        return value;
    }
}
