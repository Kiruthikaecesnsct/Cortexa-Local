using Collector.Application.Upload;
using Collector.Domain.Enums;

namespace Collector.Application.Ports;

public interface IBatchStore
{
    Task<StoredBatch> CreateAsync(NewBatch batch, CancellationToken cancellationToken);

    Task<StoredBatch?> FindByDocumentsAsync(IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken);

    Task<StoredBatch?> GetByIdAsync(string batchId, CancellationToken cancellationToken);

    Task<StoredBatch?> FindByServerBatchIdAsync(string serverBatchId, CancellationToken cancellationToken);

    Task MarkUploadingAsync(string batchId, CancellationToken cancellationToken);

    Task MarkUploadedAsync(string batchId, string serverBatchId, CancellationToken cancellationToken);

    Task MarkFailedAsync(string batchId, string error, CancellationToken cancellationToken);

    Task<IReadOnlyList<RetryableBatch>> ListRetryableAsync(CancellationToken cancellationToken);

    Task<UploadPayload?> GetPayloadAsync(string batchId, CancellationToken cancellationToken);

    Task MarkReplacedAsync(IReadOnlyCollection<string> batchIds, CancellationToken cancellationToken);

    Task MarkInterruptedAsync(CancellationToken cancellationToken);
}

public sealed record NewBatch
{
    public required string IdempotencyKey { get; init; }

    public required string BatchName { get; init; }

    public required CollectorProvider Provider { get; init; }

    public required string Model { get; init; }

    public required string PromptVersion { get; init; }

    public required IReadOnlyList<string> DocumentIds { get; init; }

    public required UploadPayload Payload { get; init; }
}

public sealed record StoredBatch
{
    public required string Id { get; init; }

    public required string IdempotencyKey { get; init; }

    public required string BatchName { get; init; }

    public required BatchStatus Status { get; init; }

    public string? ServerBatchId { get; init; }

    public string? LastError { get; init; }

    public bool IsReplaced { get; init; }

    public required IReadOnlyList<string> DocumentIds { get; init; }
}

public sealed record RetryableBatch
{
    public required string Id { get; init; }

    public required string IdempotencyKey { get; init; }

    public required string BatchName { get; init; }

    public string? LastError { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required int DocumentCount { get; init; }

    public required int ItemCount { get; init; }
}
