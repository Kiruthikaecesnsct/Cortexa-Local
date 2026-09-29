namespace Cortexa.ModelRouter.Application.DTOs;

public sealed record DualDefault(
    string Primary,
    string? Secondary);

public sealed record ModelDefaults(
    string Single,
    DualDefault Dual);

public sealed record ModelsResponse(
    IReadOnlyList<ModelInfo> Models,
    bool DualModeAvailable,
    ModelDefaults Defaults);
