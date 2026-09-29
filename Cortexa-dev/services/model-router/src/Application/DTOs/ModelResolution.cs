namespace Cortexa.ModelRouter.Application.DTOs;

public sealed record ModelResolution(
    string Provider,
    string Deployment,
    bool Enabled);
