using Collector.Application.Ports;

namespace Collector.Application.Candidates;

public sealed record InputLimitResult
{
    public required bool IsWithin { get; init; }

    public required int TokenCount { get; init; }

    public required int Limit { get; init; }

    public static InputLimitResult Within(int tokenCount, int limit) =>
        new() { IsWithin = true, TokenCount = tokenCount, Limit = limit };

    public static InputLimitResult DocumentTooLarge(int tokenCount, int limit) =>
        new() { IsWithin = false, TokenCount = tokenCount, Limit = limit };
}

public sealed class GeminiInputLimitCheck(IGeminiInputBudget budget)
{
    public async Task<InputLimitResult> CheckAsync(AiRequest request, CancellationToken cancellationToken)
    {
        var count = await budget.CountAsync(request, cancellationToken);
        var limit = budget.MaxInputTokens;
        return count > limit ? InputLimitResult.DocumentTooLarge(count, limit) : InputLimitResult.Within(count, limit);
    }
}
