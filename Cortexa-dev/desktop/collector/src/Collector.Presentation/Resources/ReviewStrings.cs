using Collector.Application.Knowledge;
using Collector.Application.Upload;
using Collector.Domain.Enums;

namespace Collector.Presentation.Resources;

public static class ReviewStrings
{
    public const string FilterAll = "All";
    public const string FilterGroupName = "Filter by kind";

    public const string KindLogic = "Logic";
    public const string KindAlgorithm = "Algorithm";
    public const string KindMethod = "Method";
    public const string KindLayer = "Layer";
    public const string KindDataModel = "Data model";
    public const string KindInterface = "Interface";
    public const string KindWorkflow = "Workflow";
    public const string KindKeyContent = "Key content";

    public const string IncludeAll = "Include all";
    public const string IncludeAllName = "Include all shown items except possible echoes";
    public const string ExcludeAll = "Exclude all";
    public const string ExcludeAllName = "Exclude all shown items";
    public const string ListName = "Knowledge items";
    public const string DetailsName = "Item details";

    public const string SourceJoin = " · ";

    public const string TagEcho = "Possible echo";
    public const string TagBlocked = "Over 500";
    public const string TagUploaded = "Uploaded";
    public const string TagFailed = "Failed";

    public const string DetailsPlaceholder = "Select an item to see its details.";
    public const string IncludeInUpload = "Include in upload";
    public const string LabelSummary = "Summary";
    public const string LabelDetails = "Details";
    public const string LabelSource = "Source";
    public const string LabelExcerpt = "Excerpt";

    public const string EchoTitle = "This item may repeat code names instead of explaining the idea.";
    public const string EchoHelp = "It starts excluded. Include it only if it reads well on its own.";
    public const string EchoReasonTitleIdentifiers = "The title is mostly names copied from the source.";
    public const string EchoReasonTitleSingle = "The title is a single name copied from the source.";
    public const string EchoReasonSummaryIdentifiers = "The summary is mostly names copied from the source.";
    public const string EchoReasonSummaryCopy = "The summary copies a long run of the source text.";

    public const string RunPartialTitle = "Some units couldn't be processed.";

    public const string NoRunTitle = "Nothing to review yet";
    public const string NoRunBody = "Parse documents on the Extract screen, then choose Extract knowledge.";
    public const string GoToExtract = "Go to Extract";
    public const string EmptyTitle = "No knowledge found";
    public const string EmptyBody = "The AI found nothing to extract. Covers, reference lists, and boilerplate often produce no items.";

    public const string NothingIncluded = "Include at least one item to upload.";

    public const string SelectionLocked =
        "Selection is locked after upload starts. Run Extract knowledge again to start a new review.";

    public const string UploadedButton = "Uploaded";
    public const string UploadedTitle = "Upload complete.";
    public const string UploadPartialTitle = "Some batches didn't upload.";
    public const string UploadFailedTitle = "Upload failed.";
    public const string ResultsTitle = "Upload results";
    public const string Copied = "Copied";
    public const string Retry = "Retry";
    public const string RetryFailed = "Retry failed batches";
    public const string ChangeSelection = "Change selection";
    public const string RetryingTitle = "Retrying failed batches…";
    public const string RetryingMessage = "Sending the saved batches again. The AI doesn't run again.";
    public const string UnlockedTitle = "Selection unlocked.";
    public const string UnlockedMessage = "Change what's included, then upload. It goes up as a new batch.";
    public const string ChangeHelp = "Choose Change selection to edit your items and upload them as a new batch.";
    public const string SelectionLockedFailed =
        "Selection is locked while failed batches wait to retry. To edit it, choose Change selection.";

    public const string ErrorInProgress = "The server is still working on this batch from an earlier try. Wait a minute, then retry.";
    public const string ErrorKeyReused = "This batch can't be retried. The server already accepted different items under its upload key.";
    public const string KeyReusedCode = "idempotency_key_reused";
    public const string ErrorSession = "Your session expired. Sign in again, then retry.";
    public const string ErrorNetwork = "Couldn't reach the Collector server. Check your connection, then retry.";
    public const string ErrorUnknown = "The batch couldn't be uploaded. Try again.";

    public static string ChipLabel(string kind, int count) => $"{kind} {count}";

    public static string ChipName(string kind, int count) => count == 1 ? $"{kind}, 1 item" : $"{kind}, {count} items";

    public static string DocCount(int included, int total) => $"{included} of {total} included";

    public static string DocShown(int shown) => $" · {shown} shown";

    public static string DocAutomationName(string file, int included, int total, bool blocked) =>
        $"{file}, {included} of {total} included" + (blocked ? ", over 500 items, can't upload" : string.Empty);

    public static string ItemAutomationName(string title, string kind, bool included, bool echo, bool blocked) =>
        $"{title}, {kind}, {(included ? "included" : "excluded")}"
        + (echo ? ", possible echo" : string.Empty)
        + (blocked ? ", document over limit" : string.Empty);

    public static string SourcePage(int page) => $"Page {page}";

    public static string SourceLines(string path, int first, int last) =>
        first == last ? $"{path} · line {first}" : $"{path} · lines {first}–{last}";

    public static string RunMeta(string provider, string model, string prompt, int items, int docs) =>
        $"{items} items from {docs} documents · {provider} · {model} · prompt {prompt}";

    public static string RunPartialMessage(int failed, int skipped) =>
        $"{failed} failed and {skipped} skipped. Items from the rest are ready to review.";

    public static string CountLine(int included, int total, int hidden) =>
        $"{included} of {total} included" + (hidden > 0 ? $" · {hidden} hidden by filter" : string.Empty);

    public static string BlockedLine(int documents) => documents == 1
        ? "1 document has more than 500 included items and won't be uploaded. Exclude items to bring it to 500 or fewer."
        : $"{documents} documents have more than 500 included items and won't be uploaded. Exclude items to bring each to 500 or fewer.";

    public static string UploadButton(int items) => items == 1 ? "Upload 1 item" : $"Upload {items} items";

    public static string UploadingProgress(int index, int total) => $"Uploading batch {index} of {total}…";

    public static string SkippedDocuments(int documents) => documents == 1
        ? "1 document couldn't be uploaded because it is too large or no longer available."
        : $"{documents} documents couldn't be uploaded because they are too large or no longer available.";

    public static string UploadedMessage(int batches) =>
        batches == 1 ? "1 batch was accepted." : $"{batches} batches were accepted.";

    public static string UploadPartialMessage(int failed, int total) =>
        $"{failed} of {total} batches failed. Retry sends the same batches again.";

    public static string BatchLine(int index, int total, int docs, int items) =>
        $"Batch {index} of {total} · {docs} docs · {items} items";

    public static string BatchIdName(int index) => $"Batch {index} ID";

    public static string CopyBatchIdName(int index) => $"Copy batch {index} ID";

    public static string ErrorRejected(string? code) => $"The server rejected this batch ({code}).";

    public static string KindLabel(KnowledgeKind kind) => kind switch
    {
        KnowledgeKind.Logic => KindLogic,
        KnowledgeKind.Algorithm => KindAlgorithm,
        KnowledgeKind.Method => KindMethod,
        KnowledgeKind.Layer => KindLayer,
        KnowledgeKind.DataModel => KindDataModel,
        KnowledgeKind.Interface => KindInterface,
        KnowledgeKind.Workflow => KindWorkflow,
        KnowledgeKind.KeyContent => KindKeyContent,
        _ => kind.ToString(),
    };

    public static string? EchoReasonText(string? reason) => reason switch
    {
        EchoReasons.TitleIsIdentifier => EchoReasonTitleSingle,
        EchoReasons.TitleIdentifierShare => EchoReasonTitleIdentifiers,
        EchoReasons.SummaryIdentifierShare => EchoReasonSummaryIdentifiers,
        EchoReasons.SummaryCopied => EchoReasonSummaryCopy,
        _ => null,
    };

    public static string CantRetryNote(int batches) => batches == 1 ? "1 batch can't be retried." : $"{batches} batches can't be retried.";

    public static string UploadErrorText(UploadError? error) => error switch
    {
        { Kind: UploadErrorKind.Session } => ErrorSession,
        { Kind: UploadErrorKind.Rejected, RejectedCode: KeyReusedCode } => ErrorKeyReused,
        { Kind: UploadErrorKind.Rejected } => ErrorRejected(error.RejectedCode),
        { Kind: UploadErrorKind.Network } => ErrorNetwork,
        { Kind: UploadErrorKind.InProgress } => ErrorInProgress,
        _ => ErrorUnknown,
    };
}
