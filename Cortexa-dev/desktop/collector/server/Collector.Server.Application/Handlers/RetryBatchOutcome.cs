namespace Collector.Server.Application.Handlers;

public sealed record RetryBatchOutcome(bool Found, IReadOnlyList<string> DocumentIds)
{
    public static RetryBatchOutcome NotFound() => new(false, []);

    public static RetryBatchOutcome Ok(IReadOnlyList<string> documentIds) => new(true, documentIds);
}
