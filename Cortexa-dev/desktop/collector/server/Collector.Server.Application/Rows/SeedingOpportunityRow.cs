using System.Text.Json.Serialization;

namespace Collector.Server.Application.Rows;

public sealed record SeedingOpportunityRow
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("grounded_in")]
    public SeedingGroundingRow? GroundedIn { get; init; }
}
