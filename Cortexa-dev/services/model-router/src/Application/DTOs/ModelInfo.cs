namespace Cortexa.ModelRouter.Application.DTOs;

public sealed record ModelInfo(
    string Id,
    string Label,
    string Provider,
    string Role,
    bool Enabled,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> AllowedStages);
