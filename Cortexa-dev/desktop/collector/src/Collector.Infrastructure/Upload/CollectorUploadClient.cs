using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collector.Application.Ports;
using Collector.Domain.Serialization;
using Collector.Domain.Upload;
using Collector.Infrastructure.Http;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Upload;

public sealed class CollectorUploadClient(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<CollectorServerOptions> server,
    ILogger<CollectorUploadClient> logger) : IKnowledgeUploadClient
{
    private const string UploadPath = "collector/batches/knowledge";
    private const string IdempotencyHeader = "Idempotency-Key";

    public async Task<KnowledgeUploadResult> UploadAsync(
        KnowledgeUploadRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var message = CreateMessage(request, idempotencyKey);
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientNames.CollectorServer);
            using var response = await client.SendAsync(message, cancellationToken);
            return await ReadAsync(response, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning("Knowledge upload failed: server unreachable.");
            throw new KnowledgeUploadException(null, null, "The Cortexa server could not be reached.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Knowledge upload failed: timed out.");
            throw new KnowledgeUploadException(null, null, "The upload timed out.", exception);
        }
    }

    private HttpRequestMessage CreateMessage(KnowledgeUploadRequest request, string idempotencyKey)
    {
        var baseUrl = server.CurrentValue.BaseUrl.TrimEnd('/');
        var message = new HttpRequestMessage(HttpMethod.Post, new Uri($"{baseUrl}/{UploadPath}"))
        {
            Content = JsonContent.Create(request, options: CollectorJson.Options),
        };
        message.Headers.TryAddWithoutValidation(IdempotencyHeader, idempotencyKey);
        return message;
    }

    private static async Task<KnowledgeUploadResult> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.OK))
        {
            throw await UploadErrorReader.ReadAsync(response, cancellationToken);
        }

        try
        {
            var result = await response.Content.ReadFromJsonAsync<KnowledgeUploadResult>(
                CollectorJson.Options,
                cancellationToken);
            return result ?? throw UnexpectedResponse((int)response.StatusCode, null);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw UnexpectedResponse((int)response.StatusCode, exception);
        }
    }

    private static KnowledgeUploadException UnexpectedResponse(int status, Exception? inner) =>
        new(status, null, "The server response could not be read.", inner);
}
