namespace Collector.Presentation.Resources;

public static class HistoryStrings
{
    public const string Title = "History";
    public const string Refresh = "Refresh history";
    public const string RefreshTip = "Refresh (F5)";
    public const string RefreshLabel = "Refresh";
    public const string Loading = "Loading history…";
    public const string LoadErrorTitle = "Couldn't load history";
    public const string SignInRequired = "Sign in to see your history.";
    public const string LoadTimedOut = "History took too long to load.";
    public const string LoadFailed = "Something went wrong while loading history.";
    public const string Retry = "Retry";
    public const string Retrying = "Retrying…";
    public const string FailedTag = "Failed";
    public const string DocumentDone = "Processed";
    public const string GoToReview = "Go to Review";
    public const string DocumentsName = "Processed documents";
    public const string ShowCandidates = "Show candidates";
    public const string HideCandidates = "Hide candidates";
    public const string CandidatesHeading = "Invention candidates";
    public const string WhyPatentable = "Why it may be patentable";
    public const string SourceExcerpt = "Source excerpt";
    public const string WhyFailed = "Why it failed";
    public const string NoCandidatesFound = "No candidates were found in this document.";
    public const string StatDocuments = "Documents";
    public const string StatProcessed = "Processed";
    public const string StatFailed = "Failed";
    public const string StatCandidates = "Candidates";
    public const string DocumentsEmptyTitle = "No processed documents yet";
    public const string DocumentsEmptyBody = "Documents you process from the Review screen are saved here for your account.";

    public static string CandidateCount(int count) => count == 1 ? "1 candidate" : $"{count} candidates";

    public static string FailureReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? "It failed." : $"Failed: {reason}.";

    public static string RetryDocument(string name) => $"Retry {name}";

    public static string SourceMissing(string name) => $"{name} can't be retried because the source file is no longer on this computer.";

    public const string DocumentNotFoundReason = "document not found";
    public const string NoCandidatesIdentified = "No invention candidates identified in this document.";

    public static string RetryFailedBecause(string name, string reason) => $"{name} failed again: {reason}.";

    public static string RetryFailed(string name) => $"{name} failed again.";

    public static string RetryDone(string name) => $"{name} was processed again.";

    public static string DeleteDocument(string name) => $"Delete {name} from history";

    public static string Deleted(string name) => $"{name} was deleted from history.";

    public static string DeleteFailed(string name) => $"{name} could not be deleted. Try again.";
}
