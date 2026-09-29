using Azure;
using Azure.Security.KeyVault.Secrets;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;

namespace Cortexa.JobOrchestrator.Infrastructure.Storage;

/// <summary>
/// Deletes the per-batch git PAT secret (<c>batch-git-pat-{batchId}</c>) when present.
/// Absence of the secret, or an unconfigured vault, is treated as success.
/// The secret value is never read or logged.
/// </summary>
public sealed class KeyVaultBatchDeleter : IBatchDeleter
{
    private const string SecretNamePrefix = "batch-git-pat-";

    private readonly SecretClient? _client;

    public KeyVaultBatchDeleter(SecretClient? client)
    {
        _client = client;
    }

    public string StoreName => "KeyVault";

    public async Task<StoreDeletionResult> DeleteAsync(string batchId, DeleteBatchContext context, CancellationToken ct)
    {
        if (_client is null)
            return new StoreDeletionResult(StoreName, 0, true);

        var secretName = $"{SecretNamePrefix}{batchId}";

        try
        {
            await _client.StartDeleteSecretAsync(secretName, ct);
            return new StoreDeletionResult(StoreName, 1, true);
        }
        catch (RequestFailedException ex) when (ex.Status is 404 or 409)
        {
            // 404: secret absent. 409: already soft-deleted by a prior partial run.
            // Both mean "nothing left to remove" — keep the operation idempotent.
            return new StoreDeletionResult(StoreName, 0, true);
        }
        catch (RequestFailedException ex)
        {
            return new StoreDeletionResult(StoreName, 0, false, ex.ErrorCode ?? ex.Status.ToString());
        }
    }
}
