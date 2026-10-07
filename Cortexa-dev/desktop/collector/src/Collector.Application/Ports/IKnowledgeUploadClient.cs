using Collector.Domain.Upload;

namespace Collector.Application.Ports;

public interface IKnowledgeUploadClient
{
    Task<KnowledgeUploadResult> UploadAsync(
        KnowledgeUploadRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed class KnowledgeUploadException : Exception
{
    public KnowledgeUploadException(int? statusCode, string? errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public int? StatusCode { get; }

    public string? ErrorCode { get; }
}
