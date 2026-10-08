namespace Collector.Infrastructure.Options;

public sealed class BedrockProviderOptions
{
    public const string SectionName = "Ai:Bedrock";

    public string SsoStartUrl { get; set; } = string.Empty;

    public string SsoRegion { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string SsoRoleName { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string Model { get; set; } = "us.anthropic.claude-sonnet-4-5-20250929-v1:0";

    public int MaxOutputTokens { get; set; } = 8192;

    public double? Temperature { get; set; }

    public int Concurrency { get; set; } = 4;

    public int MaxRetries { get; set; } = 1;

    public int TimeoutSeconds { get; set; } = 120;
}
