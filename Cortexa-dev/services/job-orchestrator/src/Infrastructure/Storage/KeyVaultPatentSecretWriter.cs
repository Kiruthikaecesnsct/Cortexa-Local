using Azure;
using Azure.Security.KeyVault.Secrets;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Domain.Entities;

namespace Cortexa.JobOrchestrator.Infrastructure.Storage;

// Only the fixed patent secret names in PatentSecretNames.All may ever be
// written or checked here — callers cannot supply an arbitrary Key Vault
// secret name through this store.
public sealed class KeyVaultPatentSecretWriter : IPatentSecretWriter
{
    private readonly SecretClient? _client;

    public KeyVaultPatentSecretWriter(SecretClient? client)
    {
        _client = client;
    }

    public async Task SetSecretAsync(string secretName, string value, CancellationToken ct)
    {
        EnsureAllowedName(secretName);

        if (_client is null)
            throw new InvalidOperationException("Key Vault is not configured. Cannot store patent API credential.");

        try
        {
            await _client.SetSecretAsync(secretName, value, ct);
        }
        catch (RequestFailedException ex)
        {
            throw new InvalidOperationException("Failed to store patent API credential in Key Vault.", ex);
        }
    }

    public async Task<bool> SecretExistsAsync(string secretName, CancellationToken ct)
    {
        EnsureAllowedName(secretName);

        if (_client is null)
            return false;

        await foreach (var properties in _client.GetPropertiesOfSecretsAsync(ct))
        {
            if (string.Equals(properties.Name, secretName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static void EnsureAllowedName(string secretName)
    {
        if (!PatentSecretNames.All.Contains(secretName, StringComparer.Ordinal))
            throw new ArgumentException($"'{secretName}' is not an allowed patent secret name.", nameof(secretName));
    }
}
