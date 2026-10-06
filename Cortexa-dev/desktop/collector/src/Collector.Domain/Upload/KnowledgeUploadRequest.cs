using System.Text.Json.Serialization;

namespace Collector.Domain.Upload;

public sealed record KnowledgeUploadRequest
{
    [JsonPropertyName("batch_name")]
    public required string BatchName { get; init; }

    [JsonPropertyName("collector")]
    public required CollectorInfo Collector { get; init; }

    [JsonPropertyName("documents")]
    public IReadOnlyList<UploadDocument> Documents { get; init; } = [];
}
