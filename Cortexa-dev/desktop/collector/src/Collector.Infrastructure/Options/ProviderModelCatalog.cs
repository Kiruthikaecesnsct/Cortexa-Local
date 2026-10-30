using Collector.Domain.Enums;

namespace Collector.Infrastructure.Options;

public sealed class ProviderModelCatalogEntry
{
    public string DefaultModel { get; set; } = string.Empty;

    public List<string> Models { get; set; } = [];
}

public sealed class ProviderModelCatalog
{
    public const string SectionName = "Ai:ModelCatalog";

    public Dictionary<CollectorProvider, ProviderModelCatalogEntry> Providers { get; set; } = new()
    {
        [CollectorProvider.Claude] = new ProviderModelCatalogEntry
        {
            DefaultModel = "claude-sonnet-5-5",
            Models = ["claude-sonnet-5-5", "claude-opus-5", "claude-haiku-5"],
        },
        [CollectorProvider.Gemini] = new ProviderModelCatalogEntry
        {
            DefaultModel = "gemini-3.6-flash",
            Models =
            [
                "gemini-3.8-flash",
                "gemini-3.6-flash",
                "gemini-3.1-flash-lite",
                "gemini-flash-latest",
                "gemini-3.5-flash-lite",
                "gemini-flash-lite-latest",
            ],
        },
        [CollectorProvider.Bedrock] = new ProviderModelCatalogEntry
        {
            DefaultModel = "us.anthropic.claude-sonnet-4-5-20250929-v1:0",
            Models =
            [
                "us.anthropic.claude-sonnet-4-5-20250929-v1:0",
                "us.anthropic.claude-opus-4-1-20250805-v1:0",
                "us.anthropic.claude-haiku-4-5-20251001-v1:0",
            ],
        },
        [CollectorProvider.Groq] = new ProviderModelCatalogEntry
        {
            DefaultModel = "openai/gpt-oss-120b",
            Models = ["openai/gpt-oss-120b", "openai/gpt-oss-20b", "qwen/qwen3.8-27b"],
        },
        [CollectorProvider.Vertex] = new ProviderModelCatalogEntry
        {
            DefaultModel = "gemini-3.6-flash",
            Models = ["gemini-3.6-flash", "gemini-3.1-flash-lite", "gemini-flash-latest"],
        },
    };
}
