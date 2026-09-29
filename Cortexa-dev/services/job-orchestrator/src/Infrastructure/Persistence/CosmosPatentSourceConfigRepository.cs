using System.Net;
using System.Text.Json.Serialization;
using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Cortexa.JobOrchestrator.Infrastructure.Persistence;

// Single "patent-sources" document in the shared `config` container. The evidence
// service reads this same document directly, so the wire shape here is a frozen
// contract: id="patent-sources", batch_id="global" (the container's physical
// partition key path is /batch_id — mirrored exactly from CosmosConfigRepository,
// which uses the same synthetic partition value for its "global" document),
// sources.{uspto,epo,lens}.enabled, config_version, updated_at, updated_by.
public sealed class CosmosPatentSourceConfigRepository : IPatentSourceConfigRepository
{
    private const string DocumentId = "patent-sources";
    private const string PartitionValue = "global";

    private readonly Container _container;

    public CosmosPatentSourceConfigRepository(CosmosClient client, IOptions<CosmosSettings> settings)
    {
        var s = settings.Value;
        _container = client.GetContainer(s.Database, s.ConfigContainer);
    }

    public async Task<PatentSourceConfig> GetAsync(CancellationToken ct)
    {
        try
        {
            var response = await _container.ReadItemAsync<PatentSourcesDocument>(
                DocumentId,
                new PartitionKey(PartitionValue),
                cancellationToken: ct);

            return MapToDomain(response.Resource);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            var defaults = CreateDefaults();
            await SeedDefaultsAsync(defaults, ct);
            return defaults;
        }
    }

    // Config_version MUST increment on every mutation (enable-flag PUTs and secret
    // writes alike) — the evidence service busts its cached patent secret on a
    // version change, so this always re-reads the current version before writing.
    public async Task<PatentSourceConfig> UpdateAsync(PatentSourceConfig desired, CancellationToken ct)
    {
        var current = await GetAsync(ct);
        var next = desired with { ConfigVersion = current.ConfigVersion + 1, UpdatedAt = DateTimeOffset.UtcNow };

        var document = MapToDocument(next);
        await _container.UpsertItemAsync(document, new PartitionKey(PartitionValue), cancellationToken: ct);
        return next;
    }

    private static PatentSourceConfig CreateDefaults()
    {
        return new PatentSourceConfig(
            Uspto: new PatentSourceFlag(true),
            Epo: new PatentSourceFlag(true),
            Lens: new PatentSourceFlag(true),
            ConfigVersion: 1,
            UpdatedAt: DateTimeOffset.UtcNow,
            UpdatedBy: "system");
    }

    private async Task SeedDefaultsAsync(PatentSourceConfig defaults, CancellationToken ct)
    {
        try
        {
            var document = MapToDocument(defaults);
            await _container.UpsertItemAsync(document, new PartitionKey(PartitionValue), cancellationToken: ct);
        }
        catch
        {
        }
    }

    private static PatentSourceConfig MapToDomain(PatentSourcesDocument doc)
    {
        return new PatentSourceConfig(
            Uspto: new PatentSourceFlag(doc.Sources.Uspto.Enabled),
            Epo: new PatentSourceFlag(doc.Sources.Epo.Enabled),
            Lens: new PatentSourceFlag(doc.Sources.Lens.Enabled),
            ConfigVersion: doc.ConfigVersion,
            UpdatedAt: doc.UpdatedAt,
            UpdatedBy: doc.UpdatedBy ?? "system");
    }

    private static PatentSourcesDocument MapToDocument(PatentSourceConfig config)
    {
        return new PatentSourcesDocument
        {
            Id = DocumentId,
            BatchId = PartitionValue,
            Sources = new SourcesSection
            {
                Uspto = new SourceFlagDocument(config.Uspto.Enabled),
                Epo = new SourceFlagDocument(config.Epo.Enabled),
                Lens = new SourceFlagDocument(config.Lens.Enabled)
            },
            ConfigVersion = config.ConfigVersion,
            UpdatedAt = config.UpdatedAt,
            UpdatedBy = config.UpdatedBy
        };
    }

    private sealed class PatentSourcesDocument
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = DocumentId;

        [JsonPropertyName("batch_id")]
        public string BatchId { get; set; } = PartitionValue;

        [JsonPropertyName("sources")]
        public SourcesSection Sources { get; set; } = new();

        [JsonPropertyName("config_version")]
        public int ConfigVersion { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }

        [JsonPropertyName("updated_by")]
        public string? UpdatedBy { get; set; }
    }

    private sealed class SourcesSection
    {
        [JsonPropertyName("uspto")]
        public SourceFlagDocument Uspto { get; set; } = new(true);

        [JsonPropertyName("epo")]
        public SourceFlagDocument Epo { get; set; } = new(true);

        [JsonPropertyName("lens")]
        public SourceFlagDocument Lens { get; set; } = new(true);
    }

    private sealed record SourceFlagDocument([property: JsonPropertyName("enabled")] bool Enabled);
}
