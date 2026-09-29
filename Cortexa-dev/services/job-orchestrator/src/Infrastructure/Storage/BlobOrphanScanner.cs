using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Storage;

public sealed class BlobOrphanScanner : IOrphanScanner
{
    private readonly BlobServiceClient _client;
    private readonly string _rawContainer;
    private readonly ILogger<BlobOrphanScanner> _logger;

    public BlobOrphanScanner(
        BlobServiceClient client,
        IOptions<BlobSettings> settings,
        ILogger<BlobOrphanScanner> logger)
    {
        _client = client;
        _rawContainer = settings.Value.RawContainer;
        _logger = logger;
    }

    public string StoreName => "blob";

    public async Task<IReadOnlyCollection<string>> ListBatchIdsAsync(CancellationToken ct)
    {
        var batchIds = new List<string>();
        var container = _client.GetBlobContainerClient(_rawContainer);

        try
        {
            await foreach (var item in container.GetBlobsByHierarchyAsync(
                BlobTraits.None,
                BlobStates.None,
                "/",
                prefix: null,
                cancellationToken: ct))
            {
                if (item.IsPrefix)
                    batchIds.Add(item.Prefix.TrimEnd('/'));
            }
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(
                "Blob orphan scan failed. container={Container} status={Status} error_code={ErrorCode}",
                _rawContainer,
                ex.Status,
                ex.ErrorCode ?? ex.Message);
        }

        return batchIds;
    }
}
