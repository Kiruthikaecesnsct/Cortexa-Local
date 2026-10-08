using Collector.Domain.History;

namespace Collector.Server.Application.Reads;

public sealed record GetBatchResultsOutcome(bool Found, BatchResults? Results)
{
    public static GetBatchResultsOutcome NotFound() => new(false, null);

    public static GetBatchResultsOutcome Ok(BatchResults results) => new(true, results);
}
