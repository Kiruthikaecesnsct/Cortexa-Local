namespace Collector.Server.Application.Reads;

public sealed record GetBatchResultsOutcome(bool Found, IReadOnlyList<BatchResultDto> Results)
{
    public static GetBatchResultsOutcome NotFound() => new(false, []);

    public static GetBatchResultsOutcome Ok(IReadOnlyList<BatchResultDto> results) => new(true, results);
}
