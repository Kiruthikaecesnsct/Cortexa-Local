using Collector.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Collector.Application.Secrets;

public sealed class StaleRemoteSecretPurge(ISecretStore secrets, ILogger<StaleRemoteSecretPurge> logger)
{
    private static readonly SecretSlot[] StaleSlots =
    [
        SecretSlot.GitHubPat,
        SecretSlot.AzureDevOpsPat,
        SecretSlot.SshPassphrase,
    ];

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var slot in StaleSlots)
        {
            await DeleteAsync(slot, cancellationToken);
        }
    }

    private async Task DeleteAsync(SecretSlot slot, CancellationToken cancellationToken)
    {
        try
        {
            await secrets.DeleteAsync(slot, cancellationToken);
        }
        catch (SecretStoreException exception)
        {
            logger.LogWarning(exception, "Could not remove the stale {Slot} secret.", slot);
        }
    }
}
