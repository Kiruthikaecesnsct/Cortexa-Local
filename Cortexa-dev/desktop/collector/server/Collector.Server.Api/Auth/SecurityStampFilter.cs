using System.Security.Claims;
using Collector.Server.Api.Endpoints;
using Collector.Server.Application.Ports;
using Collector.Server.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Collector.Server.Api.Auth;

public sealed class SecurityStampFilter(
    IUserStatusReader statusReader,
    IMemoryCache cache,
    IOptions<IdentityOptions> options,
    ILogger<SecurityStampFilter> logger) : IEndpointFilter
{
    private const string CacheKeyPrefix = "user-status:";
    private const string AccountInvalid = "account_invalid";
    private const string AccountStatusUnavailable = "account_status_unavailable";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var rejection = await CheckAsync(context.HttpContext);
        return rejection ?? await next(context);
    }

    private async Task<IResult?> CheckAsync(HttpContext http)
    {
        if (!TryReadIdentity(http.User, out var userId, out var tokenStamp))
        {
            return ErrorResponses.Forbidden(AccountInvalid, "The token is missing required identity claims.");
        }

        var status = await GetStatusAsync(userId, http.RequestAborted);
        if (status is null)
        {
            logger.LogWarning("Account status unavailable for user {UserId}; rejecting request.", userId);
            return ErrorResponses.Unavailable(AccountStatusUnavailable, "Account status could not be verified.");
        }

        return IsCurrent(status, tokenStamp) ? null : Reject(userId);
    }

    private IResult Reject(string userId)
    {
        logger.LogWarning("Rejected request for user {UserId}: account disabled or token stamp stale.", userId);
        return ErrorResponses.Forbidden(AccountInvalid, "The account is not valid for this token.");
    }

    private static bool IsCurrent(UserStatus status, Guid tokenStamp) =>
        status.Enabled && status.SecurityStamp == tokenStamp;

    private static bool TryReadIdentity(ClaimsPrincipal user, out string userId, out Guid tokenStamp)
    {
        userId = user.FindFirstValue(CollectorClaims.Subject) ?? string.Empty;
        tokenStamp = Guid.Empty;
        return Guid.TryParse(userId, out _) && CollectorClaims.TryGetStamp(user, out tokenStamp);
    }

    private async Task<UserStatus?> GetStatusAsync(string userId, CancellationToken cancellationToken)
    {
        var cacheKey = CacheKeyPrefix + userId;
        if (cache.TryGetValue(cacheKey, out UserStatus? cached))
        {
            return cached;
        }

        var status = await statusReader.GetStatusAsync(userId, cancellationToken);
        var ttlSeconds = options.Value.StatusCacheSeconds;
        if (status is not null && ttlSeconds > 0)
        {
            cache.Set(cacheKey, status, TimeSpan.FromSeconds(ttlSeconds));
        }

        return status;
    }
}
