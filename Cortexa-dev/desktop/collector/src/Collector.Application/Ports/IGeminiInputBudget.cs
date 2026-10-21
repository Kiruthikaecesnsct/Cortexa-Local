namespace Collector.Application.Ports;

public interface IGeminiInputBudget
{
    int MaxInputTokens { get; }

    Task<int> CountAsync(AiRequest request, CancellationToken cancellationToken);
}
