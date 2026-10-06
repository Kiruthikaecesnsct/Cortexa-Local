using System.Text.Json.Serialization;
using Collector.Domain.Enums;
using Collector.Domain.Knowledge;

namespace Collector.Domain.Upload;

public sealed record UploadDocument
{
    [JsonPropertyName("client_document_id")]
    public required string ClientDocumentId { get; init; }

    [JsonPropertyName("filename")]
    public required string Filename { get; init; }

    [JsonPropertyName("source_kind")]
    public required SourceKind SourceKind { get; init; }

    [JsonPropertyName("source_type")]
    public required SourceType SourceType { get; init; }

    [JsonPropertyName("knowledge_items")]
    public IReadOnlyList<KnowledgeItem> KnowledgeItems { get; init; } = [];
}
