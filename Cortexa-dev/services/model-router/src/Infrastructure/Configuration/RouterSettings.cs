using Cortexa.ModelRouter.Domain.Enums;

namespace Cortexa.ModelRouter.Infrastructure.Configuration;

public sealed class RouterSettings
{
    public string Mode { get; set; } = "single-foundry";
    public bool EnableFallback { get; set; } = true;

    public ModelMode ToModelMode() => Mode.ToLowerInvariant() switch
    {
        "single-anthropic" => ModelMode.SingleSecondary,
        "dual" => ModelMode.DualAdversarial,
        _ => ModelMode.SinglePrimary
    };
}
