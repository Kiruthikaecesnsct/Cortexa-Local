using System.Net;
using Collector.Application.Remote;
using Collector.Domain.Enums;

namespace Collector.Infrastructure.Remote.Cortexa;

public sealed class CortexaErrorMapper(TimeProvider time) : IRemoteErrorMapper
{
    public bool IsSuccess(HttpResponseMessage response) => response.IsSuccessStatusCode;

    public RemoteSourceException Map(HttpResponseMessage response) => response.StatusCode switch
    {
        HttpStatusCode.Unauthorized => Create(RemoteFailureKind.Auth),
        HttpStatusCode.Forbidden => Create(RemoteFailureKind.AccessDenied),
        HttpStatusCode.NotFound => Create(RemoteFailureKind.NotFound),
        HttpStatusCode.TooManyRequests => Create(RemoteFailureKind.RateLimited, ResetAt(response)),
        _ => Create(RemoteFailureKind.Upstream),
    };

    private DateTimeOffset? ResetAt(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return time.GetUtcNow() + delta;
        }

        return retryAfter?.Date;
    }

    private static RemoteSourceException Create(RemoteFailureKind kind, DateTimeOffset? resetAt = null) =>
        new(kind, SourceType.CortexaRepo, resetAt);
}
