using Collector.Application.Ports;
using Collector.Domain.Extraction;

namespace Collector.Application.Knowledge;

public sealed record TokenEstimate(int PromptTokens, int EstimatedOutputTokens)
{
    public int Total => PromptTokens + EstimatedOutputTokens;
}

public sealed class TokenEstimator(KnowledgePromptBuilder promptBuilder, ITokenCounter tokenCounter)
{
    private const double ExpectedOutputToPromptTokenRatio = 0.35;

    public TokenEstimate Estimate(IReadOnlyList<ExtractionUnit> units, int maxOutputTokens)
    {
        var promptTokens = SumPromptTokens(units);
        var estimatedOutputTokens = Math.Min(maxOutputTokens, ScaleToExpectedOutput(promptTokens));
        return new TokenEstimate(promptTokens, estimatedOutputTokens);
    }

    private int SumPromptTokens(IReadOnlyList<ExtractionUnit> units)
    {
        var total = 0;
        foreach (var unit in units)
        {
            total += CountPromptTokens(unit);
        }

        return total;
    }

    private int CountPromptTokens(ExtractionUnit unit)
    {
        var request = promptBuilder.Build(unit);
        return tokenCounter.Count(request.SystemText)
            + tokenCounter.Count(request.UserText)
            + tokenCounter.Count(request.OutputSchemaJson);
    }

    private static int ScaleToExpectedOutput(int promptTokens) =>
        (int)Math.Ceiling(promptTokens * ExpectedOutputToPromptTokenRatio);
}
