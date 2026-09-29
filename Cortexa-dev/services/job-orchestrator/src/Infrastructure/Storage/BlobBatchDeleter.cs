using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Storage;

/// <summary>
/// Deletes all raw blobs stored under the <c>{batchId}/</c> prefix in the raw-files container.
/// </summary>
public sealed class BlobBatchDeleter : IBatchDeleter
{
    private readonly BlobServiceClient _client;
    private readonly BlobSettings _settings;

    public BlobBatchDeleter(BlobServiceClient client, IOptions<BlobSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public string StoreName => "BlobStorage";

    public async Task<StoreDeletionResult> DeleteAsync(string batchId, DeleteBatchContext context, CancellationToken ct)
    {
        var container = _client.GetBlobContainerClient(_settings.RawContainer);
        var prefix = $"{batchId}/";
        var count = 0;

        try
        {
            await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, ct))
            {
                await container.DeleteBlobIfExistsAsync(
                    blob.Name,
                    DeleteSnapshotsOption.IncludeSnapshots,
                    cancellationToken: ct);
                count++;
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new StoreDeletionResult(StoreName, count, true);
        }
        catch (RequestFailedException ex)
        {
            return new StoreDeletionResult(StoreName, count, false, ex.ErrorCode ?? ex.Status.ToString());
        }

        return new StoreDeletionResult(StoreName, count, true);
    }
}
