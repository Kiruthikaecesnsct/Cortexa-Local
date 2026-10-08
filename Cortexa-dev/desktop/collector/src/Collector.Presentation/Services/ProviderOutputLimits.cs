using Collector.Domain.Enums;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Presentation.Services;

public sealed class ProviderOutputLimits(
    IOptions<AiProviderOptions> claude,
    IOptions<GeminiProviderOptions> gemini,
    IOptions<BedrockProviderOptions> bedrock)
{
    public int MaxOutputTokensFor(CollectorProvider provider) => provider switch
    {
        CollectorProvider.Claude => claude.Value.MaxOutputTokens,
        CollectorProvider.Gemini => gemini.Value.MaxOutputTokens,
        CollectorProvider.Bedrock => bedrock.Value.MaxOutputTokens,
        var other => throw new InvalidOperationException($"AI provider {other} is not supported."),
    };
}
