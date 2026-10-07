using Collector.Application.Upload;
using Collector.Presentation.Resources;

namespace Collector.Presentation.ViewModels;

public static class UploadOutcomeBanner
{
    public static BannerViewModel For(UploadSession? session)
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

        return session.HasFailures ? FailureBanner(results) : Create(BannerSeverity.Error, ReviewStrings.UploadFailedTitle, ReviewStrings.ErrorUnknown);
    }

    private static BannerViewModel FailureBanner(IReadOnlyList<BatchUploadResult> results)
    {
        var failed = results.Where(result => result.Status == UploadBatchStatus.Failed).ToList();
        if (failed.Count == results.Count)
        {
            return Create(BannerSeverity.Error, ReviewStrings.UploadFailedTitle, ReviewStrings.UploadErrorText(failed[0].Error));
        }

        return Create(BannerSeverity.Warning, ReviewStrings.UploadPartialTitle, ReviewStrings.UploadPartialMessage(failed.Count, results.Count));
    }

    private static string? SkippedNote(UploadSession session)
    {
        var skipped = session.Blocked.Count(blocked => blocked.Reason != BlockReason.TooManyItems);
        return skipped == 0 ? null : ReviewStrings.SkippedDocuments(skipped);
    }

    private static string Join(string message, string? note) => note is null ? message : $"{message} {note}";

    private static BannerViewModel Create(BannerSeverity severity, string title, string message) =>
        new(new BannerContent { Severity = severity, Title = title, Message = message });
}
