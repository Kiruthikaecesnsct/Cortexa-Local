namespace Cortexa.ModelRouter.Infrastructure.Configuration;

public enum FoundryTokenFieldKind
{
    MaxCompletionTokens,
    MaxTokens
}

public sealed class FoundryDeploymentOptions
{
    public FoundryTokenFieldKind TokenFieldKind { get; set; } = FoundryTokenFieldKind.MaxCompletionTokens;
    public bool SendReasoningEffort { get; set; } = true;
    public bool SendTemperature { get; set; } = true;
    public int? OutputTokenCap { get; set; }
    public int MaxInFlight { get; set; } = 24;
    public List<string>? SupportedReasoningEfforts { get; set; }
}

public sealed class FoundryTaskOverride
{
    public string? ReasoningEffort { get; set; }
    public int? OutputTokenCap { get; set; }
    public int? CallTimeoutSeconds { get; set; }
}

public sealed class FoundrySettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string Deployment { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "v1";
    public string ApiKeySecretName { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 120;
    public int CallTimeoutSeconds { get; set; } = 80;
    public int AcquireWaitSeconds { get; set; } = 2;
    public double ConcurrencyHighWaterFraction { get; set; } = 0.8;
    public string? ReasoningEffort { get; set; } = "high";
    public int MaxRetries { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 300;
    public Dictionary<string, FoundryDeploymentOptions> DeploymentOptions { get; set; } = new();
    public Dictionary<string, FoundryTaskOverride> TaskOverrides { get; set; } = new();

    public FoundryDeploymentOptions ResolveOptions(string deployment)
    {
        return DeploymentOptions.TryGetValue(deployment, out var options)
            ? options
            : new FoundryDeploymentOptions();
    }

    public FoundryTaskOverride? ResolveTaskOverride(string? taskKind)
    {
        if (string.IsNullOrWhiteSpace(taskKind))
            return null;

        return TaskOverrides.TryGetValue(taskKind, out var taskOverride)
            ? taskOverride
            : null;
    }

    public int ResolveCallTimeout(string? taskKind)
    {
        var taskOverride = ResolveTaskOverride(taskKind);
        return taskOverride?.CallTimeoutSeconds ?? CallTimeoutSeconds;
    }
}
