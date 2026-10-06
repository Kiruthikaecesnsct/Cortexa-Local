using System.Text.Json.Serialization;

namespace Collector.Domain.Upload;

public sealed record KnowledgeUploadResult
{
    [JsonPropertyName("batch_id")]
    public required string BatchId { get; init; }

    [JsonPropertyName("document_ids")]
    public IReadOnlyList<string> DocumentIds { get; init; } = [];
}
