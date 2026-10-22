using Collector.Application.Ports;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Ai;

public readonly record struct GeminiModelStep(string Model, bool IsFallback);

public sealed class GeminiModelFallback(
    IOptions<GeminiProviderOptions> options,
    IGeminiModelExhaustion exhaustion,
    IGeminiInputBudget budget)
{
    public IReadOnlyList<GeminiModelStep> Sequence(AiRequest request)
    {
        var settings = options.Value;
        var primary = string.IsNullOrWhiteSpace(request.Model) ? settings.Model : request.Model;
        var seen = new HashSet<string>(StringComparer.Ordinal) { primary };
        var steps = new List<GeminiModelStep> { new(primary, false) };
        steps.AddRange(settings.FallbackModels.Where(seen.Add).Select(model => new GeminiModelStep(model, true)));
        return steps;
    }

    public bool IsUsable(string model) => !IsEnabled || !exhaustion.IsExhausted(model);

    public void MarkExhausted(string model)
    {
        if (IsEnabled)
        {
            exhaustion.MarkExhausted(model);
        }
    }

    public async Task EnsureFitsAsync(AiRequest request, string model, CancellationToken cancellationToken)
    {
        var count = await budget.CountAsync(request with { Model = model }, cancellationToken);
        if (count > budget.MaxInputTokens)
        {
            throw new AiInputTooLargeException(count, budget.MaxInputTokens, model);
        }
    }

    private bool IsEnabled => options.Value.FallbackModels.Count > 0;
}
