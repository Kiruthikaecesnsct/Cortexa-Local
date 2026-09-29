using Azure;
using Azure.Security.KeyVault.Secrets;
using Cortexa.JobOrchestrator.Application.Interfaces;

namespace Cortexa.JobOrchestrator.Infrastructure.Storage;

public sealed class KeyVaultGitPatStore : IGitPatSecretStore
{
    private readonly SecretClient? _client;

    public KeyVaultGitPatStore(SecretClient? client)
    {
        _client = client;
    }

    public async Task<string> StoreAsync(string batchId, string pat, CancellationToken ct)
    {
        if (_client is null)
            throw new InvalidOperationException("Key Vault is not configured. Cannot store git PAT for private repository.");

        var secretName = $"batch-git-pat-{batchId}";

        try
        {
            await _client.SetSecretAsync(secretName, pat, ct);
            return secretName;
        }
        catch (RequestFailedException ex)
        {
            throw new InvalidOperationException($"Failed to store git PAT in Key Vault: {ex.Message}", ex);
        }
    }
}
