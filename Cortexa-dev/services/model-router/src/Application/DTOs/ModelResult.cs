using Cortexa.ModelRouter.Domain.ValueObjects;

namespace Cortexa.ModelRouter.Application.DTOs;

public sealed record ModelResult(
    string Provider,
    string Model,
    string Content,
    IReadOnlyList<string>? Citations,
    TokenUsage Usage,
    string? FinishReason = null);
