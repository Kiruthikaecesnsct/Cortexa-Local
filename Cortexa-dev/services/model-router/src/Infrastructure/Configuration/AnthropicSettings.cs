namespace Cortexa.ModelRouter.Infrastructure.Configuration;

public sealed class AnthropicSettings
{
    public string BaseUrl { get; set; } = "https://api.anthropic.com";
    public string Model { get; set; } = "claude-sonnet-4-6";
    public string AnthropicVersion { get; set; } = "2023-06-01";
    public string ApiVersion { get; set; } = "2025-05-01-preview";
    public string ApiKeySecretName { get; set; } = string.Empty;
}
