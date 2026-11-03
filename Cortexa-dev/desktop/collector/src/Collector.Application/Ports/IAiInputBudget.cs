using Collector.Domain.Enums;

namespace Collector.Application.Ports;

public interface IAiInputBudget
{
    int MaxInputTokens { get; }

    Task<int> CountAsync(AiRequest request, CancellationToken cancellationToken);
}

public interface IAiInputBudgetFactory
{
    IAiInputBudget Resolve(CollectorProvider provider);
}
