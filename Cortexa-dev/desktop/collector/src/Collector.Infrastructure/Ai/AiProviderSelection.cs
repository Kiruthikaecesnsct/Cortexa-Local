using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Domain.Enums;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Ai;

public sealed class AiProviderSelection(
    IServiceProvider services,
    IOptions<AiProviderOptions> claude,
    IOptions<GeminiProviderOptions> gemini,
    IOptions<BedrockProviderOptions> bedrock) : IAiProviderFactory
{
    public IAiProvider Resolve(CollectorProvider provider) => provider switch
    {
        CollectorProvider.Claude => services.GetRequiredService<ClaudeDirectProvider>(),
        CollectorProvider.Gemini => services.GetRequiredService<GeminiDirectProvider>(),
        CollectorProvider.Bedrock => services.GetRequiredService<BedrockDirectProvider>(),
        var other => throw new InvalidOperationException($"AI provider {other} is not supported."),
    };

    public int ConcurrencyFor(CollectorProvider provider) => provider switch
    {
        CollectorProvider.Claude => claude.Value.Concurrency,
        CollectorProvider.Gemini => gemini.Value.Concurrency,
        CollectorProvider.Bedrock => bedrock.Value.Concurrency,
        var other => throw new InvalidOperationException($"AI provider {other} is not supported."),
    };

    public static void Apply(KnowledgeExtractionOptions extraction, AiOptions ai) => extraction.Provider = ai.Provider;
}
