using System.Net;
using System.Text.Json.Serialization;
using Collector.Server.Application.Ports;
using Collector.Server.Application.Upload;
using Collector.Server.Infrastructure.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Collector.Server.Infrastructure.Cosmos;

public sealed class CosmosModelConfigReader(
    CosmosClient client,
    IOptions<CosmosOptions> cosmosOptions,
    IOptions<UploadOptions> uploadOptions) : IModelConfigReader
{
    private const string GlobalId = "global";

    public async Task<ModelConfigSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var cosmos = cosmosOptions.Value;
        var container = client.GetContainer(cosmos.Database, cosmos.ConfigContainer);

        try
        {
            var response = await container.ReadItemAsync<ConfigDocument>(
                GlobalId,
                new PartitionKey(GlobalId),
                cancellationToken: cancellationToken);
            return Merge(response.Resource, uploadOptions.Value.ModelDefaults);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return Merge(null, uploadOptions.Value.ModelDefaults);
        }
    }

    private static ModelConfigSnapshot Merge(ConfigDocument? document, ModelDefaultsOptions defaults) =>
        new(
            Pick(document?.ExtractionModel, defaults.ExtractionModel),
            Pick(document?.PrimaryEvidenceModel, defaults.PrimaryEvidenceModel),
            Pick(document?.ScoringModel, defaults.ScoringModel),
            Pick(document?.SeedingModel, defaults.SeedingModel),
            Pick(document?.SeedingMode, defaults.SeedingMode));

    private static string Pick(string? configured, string fallback) =>
        string.IsNullOrWhiteSpace(configured) ? fallback : configured;

    private sealed record ConfigDocument
    {
        [JsonPropertyName("Extraction_Model")]
        public string? ExtractionModel { get; init; }

        [JsonPropertyName("Primary_Evidence_Model")]
        public string? PrimaryEvidenceModel { get; init; }

        [JsonPropertyName("Scoring_Model")]
        public string? ScoringModel { get; init; }

        [JsonPropertyName("Seeding_Model")]
        public string? SeedingModel { get; init; }

        [JsonPropertyName("seeding_mode")]
        public string? SeedingMode { get; init; }
    }
}
