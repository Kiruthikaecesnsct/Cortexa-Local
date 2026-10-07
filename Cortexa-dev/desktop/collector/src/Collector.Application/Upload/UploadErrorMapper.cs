using Collector.Application.Ports;

namespace Collector.Application.Upload;

public static class UploadErrorMapper
{
    private const int Unauthorized = 401;
    private const int FirstClientError = 400;
    private const int LastClientError = 499;

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

        var isClientError = exception.StatusCode is >= FirstClientError and <= LastClientError;
        return isClientError && !string.IsNullOrWhiteSpace(exception.ErrorCode)
            ? new UploadError(UploadErrorKind.Rejected, exception.ErrorCode)
            : new UploadError(UploadErrorKind.Unknown, null);
    }

    public static string Describe(UploadError error) =>
        error.RejectedCode is null ? error.Kind.ToString().ToLowerInvariant() : $"rejected:{error.RejectedCode}";
}
