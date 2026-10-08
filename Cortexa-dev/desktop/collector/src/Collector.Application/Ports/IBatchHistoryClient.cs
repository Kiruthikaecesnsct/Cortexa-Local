using Collector.Domain.History;

namespace Collector.Application.Ports;

public interface IBatchHistoryClient
{
    Task<IReadOnlyList<BatchSummary>> ListBatchesAsync(CancellationToken cancellationToken);

    Task<BatchResults?> GetResultsAsync(string serverBatchId, CancellationToken cancellationToken);
}

public sealed class BatchHistoryException : Exception
{
    public BatchHistoryException(int? statusCode, bool isAuthFailure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        IsAuthFailure = isAuthFailure;
    }

    public int? StatusCode { get; }

    public bool IsAuthFailure { get; }
}
