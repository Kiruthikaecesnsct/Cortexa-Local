using Collector.Application.Ports;
using Collector.Domain.History;

namespace Collector.Presentation.ViewModels;

public enum FetchStatus
{
    Ok,
    NotFound,
    AuthFailed,
    Failed,
}

public readonly record struct Fetch<T>(FetchStatus Status, T? Value = default);

public static class HistoryFetch
{
    public static async Task<Fetch<IReadOnlyList<BatchSummary>>> BatchesAsync(
        IBatchHistoryClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            return new(FetchStatus.Ok, await client.ListBatchesAsync(cancellationToken));
        }
        catch (BatchHistoryException ex)
        {
            return new(Classify(ex));
        }
    }

    public static async Task<Fetch<BatchResults>> ResultsAsync(
        IBatchHistoryClient client,
        string batchId,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = await client.GetResultsAsync(batchId, cancellationToken);
            return results is null ? new(FetchStatus.NotFound) : new(FetchStatus.Ok, results);
        }
        catch (BatchHistoryException ex)
        {
            return new(Classify(ex));
        }
    }

    private static FetchStatus Classify(BatchHistoryException ex) =>
        ex.IsAuthFailure ? FetchStatus.AuthFailed : FetchStatus.Failed;
}
