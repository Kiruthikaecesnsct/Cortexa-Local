namespace Collector.Infrastructure.Remote.RateLimit;

public interface IRateHeaderReader
{
    RateSnapshot Read(HttpResponseMessage response, DateTimeOffset now);

    bool IsRateLimited(HttpResponseMessage response, RateSnapshot snapshot);
}
