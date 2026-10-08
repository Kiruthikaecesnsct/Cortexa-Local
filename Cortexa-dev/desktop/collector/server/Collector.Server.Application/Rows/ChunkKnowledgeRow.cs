using System.Text.Json.Serialization;

namespace Collector.Server.Application.Rows;

public sealed record ChunkKnowledgeRow
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("knowledge")]
    public required ChunkKnowledge Knowledge { get; init; }
}
