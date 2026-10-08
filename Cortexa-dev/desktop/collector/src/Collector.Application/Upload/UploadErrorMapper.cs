using Collector.Application.Ports;

namespace Collector.Application.Upload;

public static class UploadErrorMapper
{
    private const int Unauthorized = 401;
    private const int Conflict = 409;
    private const int FirstClientError = 400;
    private const int LastClientError = 499;
    private const string InProgressCode = "upload_in_progress";
    private const string RejectedPrefix = "rejected:";

    public static UploadError Map(KnowledgeUploadException exception)
    {
        if (exception.StatusCode is null)
        {
            return new UploadError(UploadErrorKind.Network, null);
        }

        if (exception.StatusCode == Unauthorized)
        {
            return new UploadError(UploadErrorKind.Session, null);
        }

        if (exception.StatusCode == Conflict && exception.ErrorCode == InProgressCode)
        {
            return new UploadError(UploadErrorKind.InProgress, null);
        }

        var isClientError = exception.StatusCode is >= FirstClientError and <= LastClientError;
        return isClientError && !string.IsNullOrWhiteSpace(exception.ErrorCode)
            ? new UploadError(UploadErrorKind.Rejected, exception.ErrorCode)
            : new UploadError(UploadErrorKind.Unknown, null);
    }

    public static string Describe(UploadError error) =>
        error.RejectedCode is null ? error.Kind.ToString().ToLowerInvariant() : $"{RejectedPrefix}{error.RejectedCode}";

    public static UploadError Parse(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return new UploadError(UploadErrorKind.Unknown, null);
        }

        if (description.StartsWith(RejectedPrefix, StringComparison.Ordinal) && description.Length > RejectedPrefix.Length)
        {
            return new UploadError(UploadErrorKind.Rejected, description[RejectedPrefix.Length..]);
        }

        return Enum.TryParse<UploadErrorKind>(description, ignoreCase: true, out var kind)
            ? new UploadError(kind, null)
            : new UploadError(UploadErrorKind.Unknown, null);
    }
}
