using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collector.Application.Ports;
using Collector.Domain.History;
using Collector.Domain.Serialization;
using Collector.Infrastructure.Http;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.History;

public sealed class CollectorHistoryClient(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<CollectorServerOptions> server,
    HistoryRetryPolicy retryPolicy,
    ILogger<CollectorHistoryClient> logger) : IBatchHistoryClient
{
    private const string BatchesPath = "collector/batches";
    private const string ResultsSuffix = "results";

    public async Task<IReadOnlyList<BatchSummary>> ListBatchesAsync(CancellationToken cancellationToken)
    {
        var batches = await FetchAsync<List<BatchSummary>>(BatchesPath, cancellationToken);
        return batches ?? throw new BatchHistoryException((int)HttpStatusCode.NotFound, false, "The batch list was not found.");
    }

    public Task<BatchResults?> GetResultsAsync(string serverBatchId, CancellationToken cancellationToken) =>
        FetchAsync<BatchResults>(
            $"{BatchesPath}/{Uri.EscapeDataString(serverBatchId)}/{ResultsSuffix}",
            cancellationToken);

    private async Task<T?> FetchAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(server.CurrentValue.ReadTimeoutSeconds));
        try
        {
            using var response = await SendAsync(path, timeout.Token);
            return await ReadAsync<T>(response, timeout.Token);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning("History request failed: server unreachable.");
            throw new BatchHistoryException(null, false, "The Cortexa server could not be reached.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("History request failed: timed out.");
            throw new BatchHistoryException(null, false, "The request timed out.", exception);
        }
    }

    private Task<HttpResponseMessage> SendAsync(string path, CancellationToken cancellationToken)
    {
        var uri = new Uri($"{server.CurrentValue.BaseUrl.TrimEnd('/')}/{path}");
        var client = httpClientFactory.CreateClient(HttpClientNames.CollectorServer);
        return retryPolicy.SendAsync(
            async token =>
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, uri);
                return await client.SendAsync(message, token);
            },
            cancellationToken);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var status = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
        {
            var isAuthFailure = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
            throw new BatchHistoryException(status, isAuthFailure, "The server rejected the request.");
        }

        try
        {
            var body = await response.Content.ReadFromJsonAsync<T>(CollectorJson.Options, cancellationToken);
            return body ?? throw new BatchHistoryException(status, false, "The server response could not be read.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new BatchHistoryException(status, false, "The server response could not be read.", exception);
        }
    }
}
