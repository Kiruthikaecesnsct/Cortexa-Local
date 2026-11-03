using Collector.Application.Ports;
using Collector.Domain.Enums;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Ai;

public sealed class AiInputBudgetSelection(
    IGeminiInputBudget gemini,
    ITokenCounter tokenCounter,
    IOptions<AiInputLimitOptions> limits) : IAiInputBudgetFactory
{
    public IAiInputBudget Resolve(CollectorProvider provider) =>
        provider == CollectorProvider.Gemini
            ? gemini
            : new TokenizerInputBudget(tokenCounter, LimitFor(provider));

    private int LimitFor(CollectorProvider provider) =>
        limits.Value.MaxInputTokens.TryGetValue(provider, out var limit) && limit > 0
            ? limit
            : throw new InvalidOperationException($"No input token limit is configured for {provider}.");
}
