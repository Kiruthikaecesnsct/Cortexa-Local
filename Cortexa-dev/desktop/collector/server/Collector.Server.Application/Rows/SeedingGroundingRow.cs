using System.Text.Json.Serialization;

namespace Collector.Server.Application.Rows;

public sealed record SeedingGroundingRow
{
    [JsonPropertyName("chunk_ids")]
    public IReadOnlyList<string> ChunkIds { get; init; } = [];
}
