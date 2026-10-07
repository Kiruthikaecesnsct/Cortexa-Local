namespace Collector.Infrastructure.Options;

public sealed class AiProviderOptions
{
    public const string SectionName = "Ai:Claude";

    public const string ThinkingBetweenTools = "between_tools";

    public const string ThinkingAdaptive = "adaptive";

    public const string ThinkingDisabled = "disabled";

    public string Model { get; set; } = "claude-sonnet-5-5";

    public int MaxOutputTokens { get; set; } = 8192;

    public double? Temperature { get; set; }

    public string Effort { get; set; } = "medium";

    public string Thinking { get; set; } = ThinkingBetweenTools;

    public int Concurrency { get; set; } = 4;

    public int MaxRetries { get; set; } = 1;

    public int TimeoutSeconds { get; set; } = 120;
}
