using Collector.Domain.History;
using Collector.Presentation.Resources;

namespace Collector.Presentation.ViewModels;

public enum BatchState
{
    Unknown,
    Queued,
    InProgress,
    Completed,
    Failed,
    Cancelled,
}

public enum StepStatus
{
    Pending,
    Done,
    Current,
    NotUsed,
    Failed,
    Cancelled,
}

public readonly record struct StepCounts(int Done, int Total);

public static class BatchProgress
{
    public const int StepTotal = 5;
    private const int FirstOptionalStep = 3;
    private const int IngestedStep = 0;
    private const int ScoredStep = 2;

    public static BatchState ParseState(string? raw) => Normalize(raw) switch
    {
        "queued" => BatchState.Queued,
        "inprogress" => BatchState.InProgress,
        "completed" => BatchState.Completed,
        "failed" => BatchState.Failed,
        "cancelled" or "canceled" => BatchState.Cancelled,
        _ => BatchState.Unknown,
    };

    public static string StateText(BatchState state, string raw) => state switch
    {
        BatchState.Queued => HistoryStrings.StateQueued,
        BatchState.InProgress => HistoryStrings.StateInProgress,
        BatchState.Completed => HistoryStrings.StateCompleted,
        BatchState.Failed => HistoryStrings.StateFailed,
        BatchState.Cancelled => HistoryStrings.StateCancelled,
        _ => raw,
    };

    public static string StateGlyph(BatchState state) => state switch
    {
        BatchState.Queued => Glyphs.Clock,
        BatchState.InProgress => Glyphs.Sync,
        BatchState.Completed => Glyphs.Success,
        BatchState.Failed => Glyphs.Error,
        BatchState.Cancelled => Glyphs.Close,
        _ => Glyphs.Info,
    };

    public static bool IsActive(BatchState state) => state is BatchState.Queued or BatchState.InProgress;

    public static StepStatus StatusOf(BatchSummary batch, int index)
    {
        var state = ParseState(batch.State);
        var reached = (int)batch.Stage;
        if (state == BatchState.Queued && index == IngestedStep)
        {
            return StepStatus.Current;
        }

        if (index == reached + 1 && NextStepStatus(state) is { } next)
        {
            return next;
        }

        if (index >= FirstOptionalStep && Counts(batch, index).Total == 0)
        {
            return StepStatus.NotUsed;
        }

        return index <= reached ? StepStatus.Done : StepStatus.Pending;
    }

    public static string CountText(BatchSummary batch, int index, StepStatus status) => status switch
    {
        StepStatus.Current when ParseState(batch.State) == BatchState.Queued => HistoryStrings.StepWaiting,
        StepStatus.Done or StepStatus.Current => ProgressText(batch, index),
        StepStatus.NotUsed => HistoryStrings.StepNotUsed,
        StepStatus.Failed => HistoryStrings.StepStopped,
        StepStatus.Cancelled => HistoryStrings.StepCancelled,
        _ => HistoryStrings.StepNotStarted,
    };

    public static string StageLine(BatchSummary batch)
    {
        var stage = HistoryStrings.StageName(batch.Stage);
        return ParseState(batch.State) switch
        {
            BatchState.Failed => HistoryStrings.StageLineFailed(stage),
            BatchState.InProgress => ActiveCounts(batch) is { Total: > 0 } counts
                ? stage + ReviewStrings.SourceJoin + HistoryStrings.StepCount(counts.Done, counts.Total)
                : stage,
            _ => stage,
        };
    }

    public static BatchStage? ActiveStage(BatchSummary batch)
    {
        var next = (int)batch.Stage + 1;
        return ParseState(batch.State) == BatchState.InProgress && next < StepTotal ? (BatchStage)next : null;
    }

    public static StepCounts? ActiveCounts(BatchSummary batch) =>
        ActiveStage(batch) is { } stage ? Counts(batch, (int)stage) : null;

    public static StepCounts Counts(BatchSummary batch, int index) => index switch
    {
        1 => new(batch.ExtractionCompletedCount, batch.ExtractionTotalCount),
        2 => new(batch.EmbeddingCompletedCount, batch.EmbeddingTotalCount),
        3 => new(batch.HarvestingCompletedCount, batch.HarvestingTotalCount),
        4 => new(batch.SeedingCompletedCount, batch.SeedingTotalCount),
        _ => new(0, 0),
    };

    private static StepStatus? NextStepStatus(BatchState state) => state switch
    {
        BatchState.Failed => StepStatus.Failed,
        BatchState.Cancelled => StepStatus.Cancelled,
        BatchState.InProgress => StepStatus.Current,
        _ => null,
    };

    private static string ProgressText(BatchSummary batch, int index)
    {
        if (index == IngestedStep)
        {
            return HistoryStrings.StepReceived;
        }

        var counts = Counts(batch, index);
        var text = HistoryStrings.StepCount(counts.Done, counts.Total);
        return index == ScoredStep
            ? text + ReviewStrings.SourceJoin + HistoryStrings.EvidenceCount(batch.EvidenceCompletedCount)
            : text;
    }

    private static string Normalize(string? raw) =>
        new([.. (raw ?? string.Empty).Where(char.IsLetter).Select(char.ToLowerInvariant)]);
}
