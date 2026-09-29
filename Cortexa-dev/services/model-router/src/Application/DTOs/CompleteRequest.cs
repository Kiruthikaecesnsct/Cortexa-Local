using System.Text.Json.Serialization;

namespace Cortexa.ModelRouter.Application.DTOs;

public sealed record CompleteRequest(
    [property: JsonPropertyName("task_kind")] string TaskKind,
    [property: JsonPropertyName("prompt")] string Prompt,
    [property: JsonPropertyName("evidence_refs")] IReadOnlyList<string>? EvidenceRefs,
    [property: JsonPropertyName("options")] ModelOptions? Options,
    [property: JsonPropertyName("model")] string? Model = null);
