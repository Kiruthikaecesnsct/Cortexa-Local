using Cortexa.ModelRouter.Domain.ValueObjects;

namespace Cortexa.ModelRouter.Application.DTOs;

public sealed record CompleteResponse(
    string Provider,
    string Model,
    string Content,
    IReadOnlyList<string>? Citations,
    TokenUsage? Usage,
    GroundingResult? Grounding,
    string? Error = null,
    string? FinishReason = null);
