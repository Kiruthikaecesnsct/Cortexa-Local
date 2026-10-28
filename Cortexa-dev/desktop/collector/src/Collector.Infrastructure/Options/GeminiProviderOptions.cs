namespace Collector.Infrastructure.Options;

public sealed class GeminiProviderOptions
{
    public const string SectionName = "Ai:Gemini";

    public const string ThinkingLow = "low";

    public const string ThinkingMedium = "medium";

    public const string ThinkingHigh = "high";

    public const int DefaultMaxInputTokens = 1_000_000;

    public string Model { get; set; } = "gemini-3.6-flash";

    public int MaxInputTokens { get; set; } = DefaultMaxInputTokens;

    public int MaxOutputTokens { get; set; } = 8192;

    public double? Temperature { get; set; }

    public string Thinking { get; set; } = ThinkingLow;

    public int Concurrency { get; set; } = 4;

    public int MaxRetries { get; set; } = 1;

    public int TimeoutSeconds { get; set; } = 120;

    public List<string> FallbackModels { get; set; } = [];

    public int MaxQuotaRetryDelaySeconds { get; set; } = 60;
}
