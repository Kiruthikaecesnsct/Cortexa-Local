namespace Collector.Application.Knowledge;

public enum UnitOutcomeStatus
{
    Completed,
    Skipped,
    Failed,
}

public sealed record UnitOutcome(UnitOutcomeStatus Status, IReadOnlyList<ExtractedKnowledgeItem> Items, string? Model)
{
    public static UnitOutcome Failed { get; } = new(UnitOutcomeStatus.Failed, [], null);

    public static UnitOutcome Skipped(string? model) => new(UnitOutcomeStatus.Skipped, [], model);

    public static UnitOutcome Completed(IReadOnlyList<ExtractedKnowledgeItem> items, string? model) =>
        new(UnitOutcomeStatus.Completed, items, model);
}
