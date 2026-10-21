using Collector.Application.Ports;

namespace Collector.Tests.Candidates;

internal sealed class FakeInputBudget(int count, int limit) : IGeminiInputBudget
{
    public Exception? Failure { get; init; }

    public int MaxInputTokens => limit;

    public Task<int> CountAsync(AiRequest request, CancellationToken cancellationToken) =>
        Failure is null ? Task.FromResult(count) : Task.FromException<int>(Failure);
}
