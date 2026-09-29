using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Cortexa.JobOrchestrator.Application.Contracts;

namespace Cortexa.JobOrchestrator.Infrastructure.Services;

public sealed class ModelCatalogClient : IModelCatalogClient
{
    private readonly HttpClient _httpClient;

    public ModelCatalogClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<CatalogModel>> GetModelsAsync(CancellationToken ct)
    {
        var response = await _httpClient.GetAsync("/models", ct);
        response.EnsureSuccessStatusCode();

        var catalog = await response.Content.ReadFromJsonAsync<ModelsResponse>(ct);
        if (catalog?.Models is null)
            return [];

        // Preserve catalog order so error messages list models as the router returns them.
        return catalog.Models
            .Select(m => new CatalogModel(m.Id, m.Enabled, m.AllowedStages ?? []))
            .ToList();
    }

    private sealed class ModelsResponse
    {
        [JsonPropertyName("models")]
        public List<ModelEntry>? Models { get; set; }
    }

    private sealed class ModelEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("allowedStages")]
        public List<string>? AllowedStages { get; set; }
    }
}
