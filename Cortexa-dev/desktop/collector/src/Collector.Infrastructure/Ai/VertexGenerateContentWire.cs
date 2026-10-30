using System.Text.Json;
using System.Text.Json.Serialization;

namespace Collector.Infrastructure.Ai;

internal sealed record VertexPartWire(string Text);

internal sealed record VertexContentWire(string? Role, List<VertexPartWire> Parts);

internal sealed record VertexGenerationConfigWire(
    int MaxOutputTokens,
    string ResponseMimeType,
    JsonElement ResponseSchema,
    double? Temperature);

internal sealed record VertexGenerateContentRequestWire(
    List<VertexContentWire> Contents,
    VertexContentWire SystemInstruction,
    VertexGenerationConfigWire GenerationConfig);

internal sealed record VertexCandidateWire(VertexContentWire? Content, string? FinishReason);

internal sealed record VertexGenerateContentResponseWire(List<VertexCandidateWire>? Candidates, string? ModelVersion);

internal sealed record VertexErrorDetailWire(int? Code, string? Message, string? Status);

internal sealed record VertexErrorEnvelopeWire(VertexErrorDetailWire? Error);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(VertexGenerateContentRequestWire))]
[JsonSerializable(typeof(VertexGenerateContentResponseWire))]
[JsonSerializable(typeof(VertexErrorEnvelopeWire))]
internal sealed partial class VertexJson : JsonSerializerContext;
