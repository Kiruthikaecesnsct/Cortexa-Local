using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Tests.Presentation;
using Collector.Tests.Support;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Collector.Tests.Candidates;

internal sealed class FakeInputBudget(int count, int limit) : IGeminiInputBudget
{
    public Exception? Failure { get; init; }

    public int MaxInputTokens => limit;

    public Task<int> CountAsync(AiRequest request, CancellationToken cancellationToken) =>
        Failure is null ? Task.FromResult(count) : Task.FromException<int>(Failure);
}

internal sealed class FakeInputBudgetFactory(IAiInputBudget budget) : IAiInputBudgetFactory
{
    public CollectorProvider? LastProvider { get; private set; }

    public IAiInputBudget Resolve(CollectorProvider provider)
    {
        LastProvider = provider;
        return budget;
    }
}

internal static class CandidateChoice
{
    public static AiModelChoiceService For(CollectorProvider provider) =>
        new(new FakeUserSettingsStore(), new FakeModelCatalog(), MsOptions.Create(new KnowledgeExtractionOptions { Provider = provider }));
}

internal sealed class CandidateHandlerChoice
{
    public CollectorProvider Provider { get; init; } = CollectorProvider.Gemini;

    public FakeInputBudgetFactory? BudgetFactory { get; private set; }

    public FakeInputBudgetFactory Budgets(int tokens, int limit) =>
        BudgetFactory = new FakeInputBudgetFactory(new FakeInputBudget(tokens, limit));
}
