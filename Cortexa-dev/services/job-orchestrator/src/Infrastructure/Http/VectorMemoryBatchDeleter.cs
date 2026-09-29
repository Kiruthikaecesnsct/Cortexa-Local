using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;

namespace Cortexa.JobOrchestrator.Infrastructure.Http;

public sealed class VectorMemoryBatchDeleter : IBatchDeleter
{
    private readonly HttpClient _httpClient;

    public VectorMemoryBatchDeleter(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string StoreName => "VectorMemory";

    public async Task<StoreDeletionResult> DeleteAsync(string batchId, DeleteBatchContext context, CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.DeleteAsync($"/asset/{Uri.EscapeDataString(batchId)}", ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new StoreDeletionResult(StoreName, 0, true);

            if (!response.IsSuccessStatusCode)
                return new StoreDeletionResult(StoreName, 0, false, ((int)response.StatusCode).ToString());

            var body = await response.Content.ReadFromJsonAsync<AssetDeleteResponse>(ct);
            return new StoreDeletionResult(StoreName, body?.Deleted ?? 0, true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            return new StoreDeletionResult(StoreName, 0, false, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return new StoreDeletionResult(StoreName, 0, false, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return new StoreDeletionResult(StoreName, 0, false, ex.Message);
        }
    }

    private sealed class AssetDeleteResponse
    {
        [JsonPropertyName("deleted")]
        public int Deleted { get; set; }
    }
}
