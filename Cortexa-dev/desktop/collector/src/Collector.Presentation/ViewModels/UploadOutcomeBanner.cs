using System.Windows.Input;
using Collector.Application.Upload;
using Collector.Presentation.Resources;

namespace Collector.Presentation.ViewModels;

public sealed record UploadBannerActions(ICommand Retry, ICommand ChangeSelection);

public sealed record FailureCounts(int Total, int Failed, int Retryable, UploadError? FirstError)
{
    public int CantRetry => Failed - Retryable;

    public bool AllFailed => Failed == Total;

    public static FailureCounts From(IReadOnlyList<BatchUploadResult> results)
    {
        var failed = results.Where(result => result.Status == UploadBatchStatus.Failed).ToList();
        var retryable = failed.Count(result => result.Error?.CanRetry != false);
        return new FailureCounts(results.Count, failed.Count, retryable, failed.FirstOrDefault()?.Error);
    }
}

public static class UploadOutcomeBanner
{
    public static BannerViewModel For(UploadSession? session, UploadBannerActions? actions = null)
    {
        if (session is null)
        {
            return Create(BannerSeverity.Error, ReviewStrings.UploadFailedTitle, ReviewStrings.ErrorUnknown);
        }

        var results = session.Results;
        if (results.Count == 0)
        {
            return Create(BannerSeverity.Error, ReviewStrings.UploadFailedTitle, SkippedNote(session) ?? ReviewStrings.ErrorUnknown);
        }

        if (session.IsComplete)
        {
            return Create(BannerSeverity.Success, ReviewStrings.UploadedTitle, Join(ReviewStrings.UploadedMessage(results.Count), SkippedNote(session)));
        }

        return session.HasFailures
            ? FailureBanner(FailureCounts.From(results), actions)
            : Create(BannerSeverity.Error, ReviewStrings.UploadFailedTitle, ReviewStrings.ErrorUnknown);
    }

    public static BannerViewModel Retrying() =>
        Create(BannerSeverity.Info, ReviewStrings.RetryingTitle, ReviewStrings.RetryingMessage);

    public static BannerViewModel Unlocked(ICommand dismiss) => new(new BannerContent
    {
        Severity = BannerSeverity.Info,
        Title = ReviewStrings.UnlockedTitle,
        Message = ReviewStrings.UnlockedMessage,
        DismissCommand = dismiss,
    });

    private static BannerViewModel FailureBanner(FailureCounts counts, UploadBannerActions? actions)
    {
        var severity = counts.AllFailed ? BannerSeverity.Error : BannerSeverity.Warning;
        var title = counts.AllFailed ? ReviewStrings.UploadFailedTitle : ReviewStrings.UploadPartialTitle;
        var canRetry = counts.Retryable > 0 && actions is not null;
        return new BannerViewModel(new BannerContent
        {
            Severity = severity,
            Title = title,
            Message = FailureMessage(counts),
            ActionText = canRetry ? ReviewStrings.Retry : null,
            ActionName = canRetry ? ReviewStrings.RetryFailed : null,
            ActionCommand = canRetry ? actions!.Retry : null,
            SecondaryActionText = actions is null ? null : ReviewStrings.ChangeSelection,
            SecondaryActionCommand = actions?.ChangeSelection,
        });
    }

    private static string FailureMessage(FailureCounts counts)
    {
        var body = counts.AllFailed && (counts.Failed == 1 || counts.CantRetry == 0)
            ? ReviewStrings.UploadErrorText(counts.FirstError)
            : Join(ReviewStrings.UploadPartialMessage(counts.Failed, counts.Total), CantRetryNote(counts));
        return counts.Retryable == 0 ? Join(body, ReviewStrings.ChangeHelp) : body;
    }

    private static string? CantRetryNote(FailureCounts counts) =>
        counts.CantRetry == 0 ? null : ReviewStrings.CantRetryNote(counts.CantRetry);

    private static string? SkippedNote(UploadSession session)
    {
        var skipped = session.Blocked.Count(blocked => blocked.Reason != BlockReason.TooManyItems);
        return skipped == 0 ? null : ReviewStrings.SkippedDocuments(skipped);
    }

    private static string Join(string message, string? note) => note is null ? message : $"{message} {note}";

    private static BannerViewModel Create(BannerSeverity severity, string title, string message) =>
        new(new BannerContent { Severity = severity, Title = title, Message = message });
}
