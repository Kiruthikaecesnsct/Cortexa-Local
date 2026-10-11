namespace Collector.Infrastructure.Options;

public sealed class GroqProviderOptions
{
    public const string SectionName = "Ai:Groq";

    public const string DefaultBaseUrl = "https://api.groq.com/openai/v1";

    public string Model { get; set; } = "openai/gpt-oss-120b";

    public string BaseUrl { get; set; } = DefaultBaseUrl;

    public int MaxOutputTokens { get; set; } = 8192;

    public double? Temperature { get; set; }

    public int Concurrency { get; set; } = 4;

    public int MaxRetries { get; set; } = 1;

    public int TimeoutSeconds { get; set; } = 120;
}
