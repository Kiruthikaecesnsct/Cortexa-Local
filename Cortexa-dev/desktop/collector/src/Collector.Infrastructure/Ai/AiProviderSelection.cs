using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Domain.Enums;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Ai;

public static class AiProviderSelection
{
    public static IAiProvider Resolve(IServiceProvider services) =>
        services.GetRequiredService<IOptions<AiOptions>>().Value.Provider switch
        {
            CollectorProvider.Gemini => services.GetRequiredService<GeminiDirectProvider>(),
            CollectorProvider.Claude => services.GetRequiredService<ClaudeDirectProvider>(),
            var other => throw new InvalidOperationException($"AI provider {other} is not supported."),
        };

    public static void Apply(
        KnowledgeExtractionOptions extraction,
        AiOptions ai,
        AiProviderOptions claude,
        GeminiProviderOptions gemini)
    {
        extraction.Provider = ai.Provider;
        extraction.Concurrency = ai.Provider == CollectorProvider.Gemini ? gemini.Concurrency : claude.Concurrency;
    }
}
