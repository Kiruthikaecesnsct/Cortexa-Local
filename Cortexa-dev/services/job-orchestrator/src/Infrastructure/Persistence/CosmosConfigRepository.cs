using System.Text.Json.Serialization;
using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Persistence;

public sealed class CosmosConfigRepository : IConfigRepository
{
    private const string DefaultExtractionModel = "gpt-5.5";
    private const string DefaultEvidenceModel = "gpt-5.4";
    private const string DefaultScoringModel = "gpt-5.5";
    private const string DefaultSeedingModel = "gpt-5.5";
    private const string DefaultSeedingMode = SeedingModes.Legacy;

    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> EvidenceAllowList =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-5.4"] = [],
            ["grok-4.3"] = [],
            ["DeepSeek-V4-Pro"] = [],
            ["claude-opus-4-8"] = [],
            ["claude-sonnet-4-6"] = []
        };

    private readonly Container _container;

    public CosmosConfigRepository(CosmosClient client, IOptions<CosmosSettings> settings)
    {
        var s = settings.Value;
        _container = client.GetContainer(s.Database, s.ConfigContainer);
    }

    public async Task<ModelConfig> GetAsync(CancellationToken ct)
    {
        try
        {
            var response = await _container.ReadItemAsync<ConfigDocument>(
                "global",
                new PartitionKey("global"),
                cancellationToken: ct);

            return MapToDomain(response.Resource);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var defaults = CreateDefaults();
            await SeedDefaultsAsync(defaults, ct);
            return defaults;
        }
    }

    public async Task UpdateAsync(ModelConfig config, CancellationToken ct)
    {
        var document = MapToDocument(config);
        await _container.UpsertItemAsync(document, new PartitionKey("global"), cancellationToken: ct);
    }

    private static ModelConfig CreateDefaults()
    {
        return new ModelConfig(
            ExtractionModel: DefaultExtractionModel,
            PrimaryEvidenceModel: DefaultEvidenceModel,
            ScoringModel: DefaultScoringModel,
            SeedingModel: DefaultSeedingModel,
            SeedingMode: DefaultSeedingMode,
            UpdatedAt: DateTimeOffset.UtcNow);
    }

    private async Task SeedDefaultsAsync(ModelConfig defaults, CancellationToken ct)
    {
        try
        {
            await UpdateAsync(defaults, ct);
        }
        catch
        {
        }
    }

    private static ModelConfig MapToDomain(ConfigDocument doc)
    {
        return new ModelConfig(
            ExtractionModel: doc.ExtractionModel ?? DefaultExtractionModel,
            PrimaryEvidenceModel: ResolveStoredEvidenceModel(doc.PrimaryEvidenceModel),
            ScoringModel: doc.ScoringModel ?? DefaultScoringModel,
            SeedingModel: doc.SeedingModel ?? DefaultSeedingModel,
            SeedingMode: SeedingModes.Normalize(doc.SeedingMode),
            UpdatedAt: doc.UpdatedAt);
    }

    // Legacy documents (or documents written before a stage's allow-list narrowed) may hold an
    // evidence model that is no longer permitted for the evidence stage. Coerce those back to the
    // evidence default so GET /config and the first subsequent PUT /config round-trip cleanly.
    private static string ResolveStoredEvidenceModel(string? storedModel)
    {
        if (string.IsNullOrWhiteSpace(storedModel))
            return DefaultEvidenceModel;

        return EvidenceAllowList.ContainsKey(storedModel) ? storedModel : DefaultEvidenceModel;
    }

    private static ConfigDocument MapToDocument(ModelConfig config)
    {
        return new ConfigDocument
        {
            Id = "global",
            BatchId = "global",
            ExtractionModel = config.ExtractionModel,
            PrimaryEvidenceModel = config.PrimaryEvidenceModel,
            ScoringModel = config.ScoringModel,
            SeedingModel = config.SeedingModel,
            SeedingMode = config.SeedingMode,
            UpdatedAt = config.UpdatedAt
        };
    }

    private sealed class ConfigDocument
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "global";

        [JsonPropertyName("batch_id")]
        public string BatchId { get; set; } = "global";

        [JsonPropertyName("Extraction_Model")]
        public string? ExtractionModel { get; set; }

        [JsonPropertyName("Primary_Evidence_Model")]
        public string? PrimaryEvidenceModel { get; set; }

        [JsonPropertyName("Scoring_Model")]
        public string? ScoringModel { get; set; }

        [JsonPropertyName("Seeding_Model")]
        public string? SeedingModel { get; set; }

        [JsonPropertyName("seeding_mode")]
        public string? SeedingMode { get; set; }

        [JsonPropertyName("UpdatedAt")]
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
