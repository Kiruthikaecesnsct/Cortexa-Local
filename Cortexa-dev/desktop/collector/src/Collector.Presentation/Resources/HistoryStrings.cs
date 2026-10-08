using Collector.Domain.History;
using Collector.Presentation.ViewModels;

namespace Collector.Presentation.Resources;

public static class HistoryStrings
{
    public const string Title = "History";
    public const string ListName = "Batches";
    public const string CandidatesName = "Candidates";
    public const string StagesName = "Pipeline stages";
    public const string Refresh = "Refresh history";
    public const string RefreshTip = "Refresh (F5)";
    public const string Loading = "Loading history…";
    public const string LoadingCandidates = "Loading candidates…";
    public const string LoadErrorTitle = "Couldn't load history";
    public const string LoadErrorBody = "Check your connection, then try again.";
    public const string RefreshErrorTitle = "Couldn't refresh history";
    public const string Retry = "Retry";

    public const string EmptyTitle = "No batches yet";
    public const string EmptyBody =
        "Batches you upload from the Review screen show up here, with their progress and the candidates they produce.";

    public const string GoToReview = "Go to Review";
    public const string DetailsPlaceholder = "Select a batch to see its progress and candidates.";
    public const string NotFoundTitle = "This batch isn't available";

    public const string NotFoundBody =
        "The server couldn't find it for your account. It may have been removed, or it belongs to a different account.";

    public const string NoCandidatesYetTitle = "No candidates yet";
    public const string NoCandidatesYetBody = "Candidates appear after the Harvested or Seeded stage. This page updates on its own.";
    public const string NoCandidatesDoneTitle = "No candidates found";
    public const string NoCandidatesDoneBody = "The server finished this batch without finding any candidates.";
    public const string NoCandidatesStoppedTitle = "No candidates";

    public const string StateQueued = "Queued";
    public const string StateInProgress = "In progress";
    public const string StateCompleted = "Completed";
    public const string StateFailed = "Failed";
    public const string StateCancelled = "Cancelled";

    public const string StageIngested = "Ingested";
    public const string StageExtracted = "Extracted";
    public const string StageScored = "Scored";
    public const string StageHarvested = "Harvested";
    public const string StageSeeded = "Seeded";

    public const string StepReceived = "Received";
    public const string StepWaiting = "Waiting";
    public const string StepNotStarted = "Not started";
    public const string StepNotUsed = "Not used";
    public const string StepStopped = "Stopped here";
    public const string StepCancelled = "Cancelled";

    public const string BatchIdLabel = "Batch ID";
    public const string CopyBatchId = "Copy batch ID";
    public const string EngineHarvesting = "Harvesting";
    public const string EngineSeeding = "Seeding";
    public const string LabelScore = "Score";
    public const string LabelPatentability = "Patentability";
    public const string LabelEvidence = "Evidence";
    public const string NullMetric = "—";
    public const string NotScored = "Not scored yet";
    public const string NotRated = "Not rated yet";
    public const string NoLinks = "No linked knowledge items.";
    public const string OpenSource = "Open source";
    public const string SourceNotFound = "Not found on this computer";

    public static string Polling(DateTimeOffset time, int seconds) =>
        $"Updating every {Seconds(seconds)} · Last updated {time:T}";

    public static string Updated(DateTimeOffset time) => $"Last updated {time:T}";

    public static string RefreshErrorBody(DateTimeOffset time, int seconds) =>
        $"Showing what loaded at {time:T}. Trying again in {Seconds(seconds)}.";

    public static string RefreshErrorBodyStatic(DateTimeOffset time) => $"Showing what loaded at {time:T}.";

    public static string NoCandidatesStoppedBody(string stage) =>
        $"This batch stopped after the {stage} stage, so it produced no candidates.";

    public static string StepCount(int done, int total) => $"{done} of {total}";

    public static string EvidenceCount(int count) => $"{count} evidence";

    public static string StageLineFailed(string stage) => $"Failed after {stage}";

    public static string Uploaded(DateTimeOffset date) => $"Uploaded {date:g}";

    public static string CandidateCount(int count) => count == 1 ? "1 candidate" : $"{count} candidates";

    public static string LinkedCount(int count) => count == 1 ? "1 linked item" : $"{count} linked items";

    public static string OpenSourceName(string title) => $"Open source for {title}";

    public static string AnnounceStage(string batch, string stage) => $"{batch} reached {stage}";

    public static string AnnounceCompleted(string batch) => $"{batch} completed";

    public static string AnnounceFailed(string batch, string stage) => $"{batch} failed after {stage}";

    public static string ProgressName(string stage) => $"{stage} progress";

    public static string StageName(BatchStage stage) => stage switch
    {
        BatchStage.Ingested => StageIngested,
        BatchStage.Extracted => StageExtracted,
        BatchStage.Scored => StageScored,
        BatchStage.Harvested => StageHarvested,
        _ => StageSeeded,
    };

    public static string BatchAutomationName(string name, string state, string stage, string stageLine, string created) =>
        $"{name}, {state}, stage {stage}, {stageLine}, uploaded {created}";

    public static string StepAutomationName(string stage, string status, string count) => $"{stage}, {status}, {count}";

    public static string CandidateAutomationName(string title, string engine, string kind, CandidateMetrics metrics) =>
        $"{title}, {engine}, {kind}, score {metrics.Score ?? NotScored.ToLowerInvariant()}, "
        + $"patentability {metrics.Patentability ?? NotRated.ToLowerInvariant()}, {EvidenceCount(metrics.Evidence)}, "
        + LinkedCount(metrics.Links).ToLowerInvariant();

    public static string StatusName(StepStatus status) => status switch
    {
        StepStatus.Done => "complete",
        StepStatus.Current => "in progress",
        StepStatus.NotUsed => "not used",
        StepStatus.Failed => "failed",
        StepStatus.Cancelled => "cancelled",
        _ => "pending",
    };

    private static string Seconds(int seconds) => seconds == 1 ? "1 second" : $"{seconds} seconds";
}

public readonly record struct CandidateMetrics(string? Score, string? Patentability, int Evidence, int Links);
