using Collector.Application.Ports;

namespace Collector.Application.Knowledge;

public enum UnitOutcomeStatus
{
    Completed,
    Skipped,
    Failed,
}

public sealed record UnitOutcome(
    UnitOutcomeStatus Status,
    IReadOnlyList<ExtractedKnowledgeItem> Items,
    string? Model,
    AiFailureKind? FailureKind = null)
{
    public static UnitOutcome Failed(AiFailureKind? kind = null) => new(UnitOutcomeStatus.Failed, [], null, kind);

    public static UnitOutcome Skipped(string? model) => new(UnitOutcomeStatus.Skipped, [], model);

    public static UnitOutcome Completed(IReadOnlyList<ExtractedKnowledgeItem> items, string? model) =>
        new(UnitOutcomeStatus.Completed, items, model);
}
