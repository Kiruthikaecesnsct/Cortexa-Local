using System.Text.Json.Serialization;

namespace Collector.Domain.History;

public sealed record BatchResults
{
    [JsonPropertyName("batch_id")]
    public required string BatchId { get; init; }

    [JsonPropertyName("candidates")]
    public required IReadOnlyList<BatchCandidate> Candidates { get; init; }
}
