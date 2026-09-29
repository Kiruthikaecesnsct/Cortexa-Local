using System.Net;
using System.Text.Json.Serialization;
using Azure;
using Azure.Storage.Blobs;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Storage;

public sealed class ViewableDocumentReader : IViewableDocumentReader
{
    private readonly CosmosClient _cosmosClient;
    private readonly CosmosSettings _cosmosSettings;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobSettings _blobSettings;

    public ViewableDocumentReader(
        CosmosClient cosmosClient,
        IOptions<CosmosSettings> cosmosSettings,
        BlobServiceClient blobServiceClient,
        IOptions<BlobSettings> blobSettings)
    {
        _cosmosClient = cosmosClient;
        _cosmosSettings = cosmosSettings.Value;
        _blobServiceClient = blobServiceClient;
        _blobSettings = blobSettings.Value;
    }

    private Container DocumentsContainer =>
        _cosmosClient.GetContainer(_cosmosSettings.Database, _cosmosSettings.DocumentsContainer);

    public async Task<ViewableDocumentLookupResult> GetAsync(string batchId, string documentId, CancellationToken ct)
    {
        var document = await ReadDocumentAsync(batchId, documentId, ct);
        if (document is null)
            return ViewableDocumentLookupResult.NotFound;

        if (string.IsNullOrWhiteSpace(document.ViewableBlobUri))
            return ViewableDocumentLookupResult.NoArtifact;

        return await DownloadViewableBlobAsync(document, ct);
    }

    private async Task<CosmosViewableDocumentProjection?> ReadDocumentAsync(
        string batchId,
        string documentId,
        CancellationToken ct)
    {
        try
        {
            var response = await DocumentsContainer.ReadItemAsync<CosmosViewableDocumentProjection>(
                documentId,
                new PartitionKey(batchId),
                cancellationToken: ct);

            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<ViewableDocumentLookupResult> DownloadViewableBlobAsync(
        CosmosViewableDocumentProjection document,
        CancellationToken ct)
    {
        var blobClient = ResolveBlobClient(document.ViewableBlobUri!);

        try
        {
            var response = await blobClient.DownloadStreamingAsync(cancellationToken: ct);
            var download = response.Value;
            var contentType = download.Details.ContentType is { Length: > 0 } declared ? declared : "application/pdf";
            var fileName = document.Filename is { Length: > 0 } name ? name : $"{document.Id}.pdf";
            return ViewableDocumentLookupResult.Found(download.Content, contentType, fileName);
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return ViewableDocumentLookupResult.NoArtifact;
        }
    }

    private BlobClient ResolveBlobClient(string viewableBlobUri)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(_blobSettings.ViewableContainer);
        var blobName = ResolveBlobName(viewableBlobUri);
        return containerClient.GetBlobClient(blobName);
    }

    private static string ResolveBlobName(string viewableBlobUri)
    {
        var uri = new Uri(viewableBlobUri);
        var trimmedPath = uri.AbsolutePath.TrimStart('/');
        var segments = trimmedPath.Split('/', 2);
        return segments.Length == 2 ? segments[1] : trimmedPath;
    }

    private sealed class CosmosViewableDocumentProjection
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("batch_id")] public string BatchId { get; set; } = string.Empty;
        [JsonPropertyName("filename")] public string Filename { get; set; } = string.Empty;
        [JsonPropertyName("viewable_blob_uri")] public string? ViewableBlobUri { get; set; }
    }
}
