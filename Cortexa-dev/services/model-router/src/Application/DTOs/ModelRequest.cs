using Cortexa.ModelRouter.Domain.Enums;

namespace Cortexa.ModelRouter.Application.DTOs;

public sealed record ModelRequest(
    ModelMode Mode,
    string TaskKind,
    string Prompt,
    IReadOnlyList<string>? EvidenceRefs,
    ModelOptions Options,
    string? Model = null);
