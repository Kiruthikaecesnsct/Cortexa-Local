using System.Net;
using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Infrastructure.Remote.RateLimit;

namespace Collector.Infrastructure.Remote.GitHub;

public sealed class GitHubErrorMapper(GitHubRateHeaders headers, TimeProvider time) : IRemoteErrorMapper
{
    private const string SsoHeader = "x-github-sso";

    public bool IsSuccess(HttpResponseMessage response) => response.IsSuccessStatusCode;

    public RemoteSourceException Map(HttpResponseMessage response) => response.StatusCode switch
    {
        HttpStatusCode.Unauthorized => Create(RemoteFailureKind.Auth),
        HttpStatusCode.Forbidden => MapForbidden(response),
        HttpStatusCode.NotFound => Create(RemoteFailureKind.NotFound),
        HttpStatusCode.Conflict => Create(RemoteFailureKind.EmptyRepository),
        HttpStatusCode.TooManyRequests => RateLimited(response),
        _ => Create(RemoteFailureKind.Upstream),
    };

    private RemoteSourceException MapForbidden(HttpResponseMessage response)
    {
        if (response.Headers.Contains(SsoHeader))
        {
            return Create(RemoteFailureKind.SsoRequired);
        }

        var snapshot = headers.Read(response, time.GetUtcNow());
        return headers.IsRateLimited(response, snapshot)
            ? RateLimited(response)
            : Create(RemoteFailureKind.AccessDenied);
    }

    private RemoteSourceException RateLimited(HttpResponseMessage response)
    {
        var now = time.GetUtcNow();
        var snapshot = headers.Read(response, now);
        return Create(RemoteFailureKind.RateLimited, snapshot.ResetAt ?? now + snapshot.RetryAfter);
    }

    private static RemoteSourceException Create(RemoteFailureKind kind, DateTimeOffset? resetAt = null) =>
        new(kind, SourceType.Github, resetAt);
}
