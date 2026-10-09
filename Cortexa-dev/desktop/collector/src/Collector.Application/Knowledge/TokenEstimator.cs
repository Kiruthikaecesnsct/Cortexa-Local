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
        return FromPromptTokens(CountPromptTokens(units), maxOutputTokens);
    }

    public TokenEstimate FromPromptTokens(int promptTokens, int maxOutputTokens)
    {
        var estimatedOutputTokens = Math.Min(maxOutputTokens, ScaleToExpectedOutput(promptTokens));
        return new TokenEstimate(promptTokens, estimatedOutputTokens);
    }

    public int CountPromptTokens(IReadOnlyList<ExtractionUnit> units)
    {
        var total = 0;
        foreach (var unit in units)
        {
            total += CountUnitPromptTokens(unit);
        }

        return total;
    }

    private int CountUnitPromptTokens(ExtractionUnit unit)
    {
        var request = promptBuilder.Build(unit);
        return tokenCounter.Count(request.SystemText)
            + tokenCounter.Count(request.UserText)
            + tokenCounter.Count(request.OutputSchemaJson);
    }

    private static int ScaleToExpectedOutput(int promptTokens) =>
        (int)Math.Ceiling(promptTokens * ExpectedOutputToPromptTokenRatio);
}
