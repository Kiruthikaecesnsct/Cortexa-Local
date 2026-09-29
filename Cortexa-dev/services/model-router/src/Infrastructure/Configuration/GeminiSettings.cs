namespace Cortexa.ModelRouter.Infrastructure.Configuration;

public sealed class GeminiSettings
{
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com";
    public string ApiVersion { get; set; } = "v1beta";
    public string Model { get; set; } = "gemini-3.1-pro-preview";
    public string ApiKeySecretName { get; set; } = "gemini-api-key";
    public int TimeoutSeconds { get; set; } = 120;
}
