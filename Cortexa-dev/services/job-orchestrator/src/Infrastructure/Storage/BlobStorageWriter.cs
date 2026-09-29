using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Storage;

public sealed class BlobStorageWriter : IBlobStorageWriter
{
    private readonly BlobServiceClient _client;
    private readonly BlobSettings _settings;

    public BlobStorageWriter(BlobServiceClient client, IOptions<BlobSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<string> SaveRawAsync(string batchId, string documentId, Stream content, string contentType, CancellationToken ct)
    {
        var container = _client.GetBlobContainerClient(_settings.RawContainer);
        var blobClient = container.GetBlobClient($"{batchId}/{documentId}");

        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        };

        try
        {
            await blobClient.UploadAsync(content, uploadOptions, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
        }

        return blobClient.Uri.ToString();
    }
}
