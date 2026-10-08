using System.Net;

namespace Collector.Infrastructure.Remote.RateLimit;

public sealed class GitHubRateHeaders() : RateHeaderReaderBase("x-ratelimit-remaining", "x-ratelimit-reset")
{
    private const string SsoHeader = "x-github-sso";

    public override bool IsRateLimited(HttpResponseMessage response, RateSnapshot snapshot) =>
        base.IsRateLimited(response, snapshot) || IsForbiddenByLimit(response, snapshot);

    private static bool IsForbiddenByLimit(HttpResponseMessage response, RateSnapshot snapshot) =>
        response.StatusCode == HttpStatusCode.Forbidden
        && !response.Headers.Contains(SsoHeader)
        && (snapshot.Remaining == 0 || snapshot.RetryAfter is not null);
}
