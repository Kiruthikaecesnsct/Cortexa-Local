namespace Collector.Infrastructure.Options;

public sealed class VertexProviderOptions
{
    public const string SectionName = "Ai:Vertex";

    public const string DefaultLocation = "us-central1";

    public string ProjectId { get; set; } = string.Empty;

    public string Location { get; set; } = DefaultLocation;

    public string Model { get; set; } = "gemini-3.6-flash";

    public int MaxOutputTokens { get; set; } = 8192;

    public double? Temperature { get; set; }

    public int Concurrency { get; set; } = 4;

    public int MaxRetries { get; set; } = 1;

    public int TimeoutSeconds { get; set; } = 120;

    public int TokenRefreshSkewSeconds { get; set; } = 300;
}
