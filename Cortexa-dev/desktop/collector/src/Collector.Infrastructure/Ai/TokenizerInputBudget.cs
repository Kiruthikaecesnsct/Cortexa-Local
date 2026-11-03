using Collector.Application.Ports;

namespace Collector.Infrastructure.Ai;

public sealed class TokenizerInputBudget(ITokenCounter tokenCounter, int maxInputTokens) : IAiInputBudget
{
    public int MaxInputTokens => maxInputTokens;

    public Task<int> CountAsync(AiRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(tokenCounter.Count(request.SystemText) + tokenCounter.Count(request.UserText));
}
