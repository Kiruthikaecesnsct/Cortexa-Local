namespace Collector.Presentation.Resources;

public static class CandidateStrings
{
    public const string ProgressTitle = "Finding invention candidates";
    public const string ProgressName = "Candidate progress";
    public const string CancelName = "Cancel candidate search";
    public const string PanelTitle = "Invention candidates";
    public const string PanelCaption = "Found on this computer. Not uploaded.";
    public const string EmptyText = "No candidates found for the uploaded file.";
    // Knowledge extraction is disabled by product decision; restore these inline texts when re-enabling it.
    // public const string KnowledgeEmpty = "No knowledge items came from this run.";
    // public const string KnowledgeFailedInline = "Knowledge extraction did not finish. Candidates are shown below.";
    public const string LabelWhy = "Why it may be patentable";
    public const string LabelSource = "Source file";
    public const string LabelExcerpt = "Excerpt";
    public const string TagUnverified = "Unverified";
    public const string UnverifiedHelp = "This excerpt was not found in the source file. Check it before you rely on it.";

    public const string KeyMissingTitle = "Add an AI key to find candidates.";
    public const string KeyMissingMessage = "Candidate search uses your own AI key.";
    public const string OpenSettingsName = "Open Settings, Provider keys";
    public const string KeyRejectedTitle = "Your AI key was rejected.";
    public const string KeyRejectedMessage = "Check the key in Settings, then try again.";
    public const string AllKeysFailedTitle = "All of your AI keys failed.";
    public const string QuotaTitle = "AI rate limit or quota reached.";
    public const string QuotaMessage = "Try again later.";
    public const string NetworkTitle = "Candidate search couldn't reach the AI service.";
    public const string NetworkMessage = "Check your connection, then try again.";
    public const string FailedTitle = "Candidate search failed.";
    public const string CanceledTitle = "Candidate search canceled.";
    public const string PartialSuffix = "Candidates from finished documents are shown.";

    public static string ProgressText(int completed, int total) => $"Candidates: {completed} of {total} documents";

    public static string CountText(int count) => count == 1 ? "1 candidate" : $"{count} candidates";

    public static string ExcerptRange(int start, int end) => $"Characters {start:N0} to {end:N0}";

    public static string ItemName(string title, string file, bool unverified) =>
        $"{title}. From {file}.{(unverified ? " Excerpt unverified." : string.Empty)}";

    public static string TooLargeTitle(int count) => count == 1
        ? "1 document is too large for candidate search."
        : $"{count} documents are too large for candidate search.";

    public static string TooLargeTokens(string file, int count, int limit) =>
        $"{file}: {count:N0} tokens, limit {limit:N0}";

    public static string TooLargeSize(string file, string size, string cap) => $"{file}: {size} MB, limit {cap} MB";

    public static string TooLargeMore(int count) => $"and {count} more.";

    public static string AllKeysFailedMessage(int keyCount) =>
        $"Each of your {keyCount} keys was rejected or hit its quota. Check them in Settings or try later.";

    public static string FailedMessage(int count) => $"{count} documents could not be processed. Try again.";

    public static string CanceledMessage(int documents) => $"Candidates from {documents} finished documents are kept.";
}
