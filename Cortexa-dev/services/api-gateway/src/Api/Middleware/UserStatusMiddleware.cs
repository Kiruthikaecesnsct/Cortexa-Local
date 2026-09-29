using System.Security.Claims;
using Cortexa.ApiGateway.Api.Auth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cortexa.ApiGateway.Api.Middleware;

public sealed class UserStatusMiddleware
{
    private const string SubjectClaimType = "sub";
    private const string SecurityStampClaimType = "stamp";
    private const string CacheKeyPrefix = "user-status:";

    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly UserStatusSettings _settings;
    private readonly ILogger<UserStatusMiddleware> _logger;

    public UserStatusMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        IOptions<UserStatusSettings> settings,
        ILogger<UserStatusMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _settings = settings.Value;
        _logger = logger;
    }

    // IUserStatusClient is resolved lazily from context.RequestServices after ShouldSkip() passes,
    // not via method or constructor injection — UseMiddleware<T> resolves every InvokeAsync
    // parameter via DI before the method body runs, which would build the underlying typed
    // HttpClient (and its configured BaseAddress) for every request, including anonymous,
    // unauthenticated, and /health traffic that never needs it. Resolving it here, per request,
    // also keeps handler rotation intact — the client is never captured in the constructor.
    public async Task InvokeAsync(HttpContext context)
    {
        if (ShouldSkip(context))
        {
            await _next(context);
            return;
        }

        var userId = context.User.FindFirstValue(SubjectClaimType);
        var tokenStamp = ParseTokenStamp(context.User);

        if (string.IsNullOrWhiteSpace(userId) || tokenStamp is null)
        {
            await WriteForbiddenAsync(context);
            return;
        }

        var userStatusClient = context.RequestServices.GetRequiredService<IUserStatusClient>();
        var status = await ResolveStatusAsync(userStatusClient, userId, context.RequestAborted);
        if (status is null)
        {
            await WriteServiceUnavailableAsync(context);
            return;
        }

        if (!IsTokenStillValid(status, tokenStamp.Value))
        {
            await WriteForbiddenAsync(context);
            return;
        }

        await _next(context);
    }

    private static bool ShouldSkip(HttpContext context)
    {
        var isSkipped = context.Items.TryGetValue("SkipAuthorization", out var skip) && skip is true;
        return isSkipped || context.User.Identity?.IsAuthenticated != true;
    }

    private static Guid? ParseTokenStamp(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(SecurityStampClaimType);
        return Guid.TryParse(raw, out var stamp) ? stamp : null;
    }

    private static bool IsTokenStillValid(UserStatusResult status, Guid tokenStamp)
        => status.Enabled && status.SecurityStamp == tokenStamp;

    // Cache TTL is intentionally short (config-driven, default single-digit seconds): it bounds
    // how long a disablement or role/permission change takes to propagate to the gateway,
    // trading a small window of staleness for not hammering the identity service on every request.
    private async Task<UserStatusResult?> ResolveStatusAsync(
        IUserStatusClient userStatusClient,
        string userId,
        CancellationToken cancellationToken)
    {
        var cacheKey = CacheKeyPrefix + userId;
        if (_cache.TryGetValue(cacheKey, out UserStatusResult? cached) && cached is not null)
        {
            return cached;
        }

        var fetched = await userStatusClient.GetStatusAsync(userId, cancellationToken);
        if (fetched is null)
        {
            _logger.LogWarning("No cached user-status for {UserId} and identity lookup failed; failing closed", userId);
            return null;
        }

        _cache.Set(cacheKey, fetched, TimeSpan.FromSeconds(_settings.CacheTtlSeconds));
        return fetched;
    }

    private static Task WriteForbiddenAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(new
        {
            error = "account_invalid",
            message = "Account is disabled or the session was invalidated by a security-relevant change."
        });
    }

    // Fail-closed on identity outage: with no cached entry to fall back on, silently letting a
    // possibly-disabled user through is worse than a transient rejection, so this returns 503
    // (distinct from the 403 above) to signal a retryable infrastructure failure rather than a
    // policy decision.
    private static Task WriteServiceUnavailableAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return context.Response.WriteAsJsonAsync(new
        {
            error = "account_status_unavailable",
            message = "Unable to verify account status. Please retry."
        });
    }
}
