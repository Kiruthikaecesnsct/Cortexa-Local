using Azure;
using Azure.Security.KeyVault.Secrets;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Cortexa.JobOrchestrator.Infrastructure.Storage;

public sealed class KeyVaultOrphanScanner : IOrphanScanner
{
    private const string SecretNamePrefix = "batch-git-pat-";

    private readonly SecretClient? _client;
    private readonly ILogger<KeyVaultOrphanScanner> _logger;

    public KeyVaultOrphanScanner(SecretClient? client, ILogger<KeyVaultOrphanScanner> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string StoreName => "keyvault";

    public async Task<IReadOnlyCollection<string>> ListBatchIdsAsync(CancellationToken ct)
    {
        if (_client is null)
            return [];

        var batchIds = new List<string>();

        try
        {
            await foreach (var properties in _client.GetPropertiesOfSecretsAsync(ct))
            {
                if (properties.Name.StartsWith(SecretNamePrefix, StringComparison.Ordinal))
                    batchIds.Add(properties.Name[SecretNamePrefix.Length..]);
            }
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(
                "Key Vault orphan scan failed. status={Status} reason={Reason}",
                ex.Status,
                ex.ErrorCode ?? ex.Message);
        }

        return batchIds;
    }
}
