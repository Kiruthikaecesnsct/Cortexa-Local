using System.Net;
using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Infrastructure.Remote.RateLimit;

namespace Collector.Infrastructure.Remote.AzureDevOps;

public sealed class AzureDevOpsErrorMapper(AzureDevOpsRateHeaders headers, TimeProvider time) : IRemoteErrorMapper
{
    public bool IsSuccess(HttpResponseMessage response) =>
        response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NonAuthoritativeInformation;

    public RemoteSourceException Map(HttpResponseMessage response)
    {
        if (IsAuthFailure(response.StatusCode))
        {
            return Create(RemoteFailureKind.Auth);
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Forbidden => Create(RemoteFailureKind.AccessDenied),
            HttpStatusCode.NotFound => Create(RemoteFailureKind.NotFound),
            HttpStatusCode.TooManyRequests => RateLimited(response),
            _ => Create(RemoteFailureKind.Upstream),
        };
    }

    private static bool IsAuthFailure(HttpStatusCode status) =>
        status is HttpStatusCode.Unauthorized or HttpStatusCode.NonAuthoritativeInformation
        || (int)status is >= 300 and < 400;

    private RemoteSourceException RateLimited(HttpResponseMessage response)
    {
        var now = time.GetUtcNow();
        var snapshot = headers.Read(response, now);
        return Create(RemoteFailureKind.RateLimited, snapshot.ResetAt ?? now + snapshot.RetryAfter);
    }

    private static RemoteSourceException Create(RemoteFailureKind kind, DateTimeOffset? resetAt = null) =>
        new(kind, SourceType.AzureDevops, resetAt);
}
