using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Domain.Exceptions;

namespace Cortexa.Identity.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/auth/login", async (
            LoginRequest request,
            LoginHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            try
            {
                var (response, rawRefreshToken) = await handler.HandleAsync(request, ct);
                SetRefreshTokenCookie(httpContext, rawRefreshToken);
                return ApiEnvelope.Success(response);
            }
            catch (UnauthorizedException)
            {
                return ApiEnvelope.Error(401, "INVALID_CREDENTIALS", "Invalid credentials");
            }
            catch (AccountLockedException ex)
            {
                return AccountLockedResult(httpContext, ex.RetryAfter);
            }
            catch (ForbiddenException ex)
            {
                return ApiEnvelope.Error(403, "FORBIDDEN", ex.Message);
            }
        })
        .RequireRateLimiting("login");

        app.MapPost("/auth/refresh", async (
            RefreshHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var rawRefreshToken = httpContext.Request.Cookies["refresh_token"];
            if (string.IsNullOrEmpty(rawRefreshToken))
                return ApiEnvelope.Error(401, "INVALID_REFRESH_TOKEN", "Invalid refresh token");

            try
            {
                var (response, newRawRefreshToken) = await handler.HandleAsync(rawRefreshToken, ct);
                SetRefreshTokenCookie(httpContext, newRawRefreshToken);
                return ApiEnvelope.Success(response);
            }
            catch (UnauthorizedException)
            {
                return ApiEnvelope.Error(401, "INVALID_REFRESH_TOKEN", "Invalid refresh token");
            }
            catch (ForbiddenException ex)
            {
                return ApiEnvelope.Error(403, "FORBIDDEN", ex.Message);
            }
        })
        .RequireRateLimiting("refresh");

        app.MapPost("/auth/entra", async (
            EntraLoginRequest request,
            EntraLoginHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            try
            {
                var (response, rawRefreshToken) = await handler.HandleAsync(request, ct);
                SetRefreshTokenCookie(httpContext, rawRefreshToken);
                return ApiEnvelope.Success(response);
            }
            catch (UnauthorizedException)
            {
                return ApiEnvelope.Error(401, "INVALID_TOKEN", "Invalid token");
            }
        })
        .RequireRateLimiting("entra");

        app.MapPost("/auth/logout", async (
            LogoutHandler handler,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            ExpireRefreshTokenCookie(httpContext);
            var rawRefreshToken = httpContext.Request.Cookies["refresh_token"];
            if (!string.IsNullOrEmpty(rawRefreshToken))
            {
                try { await handler.HandleAsync(rawRefreshToken, ct); }
                catch { /* best-effort revocation; cookie already expired */ }
            }
            return Results.NoContent();
        })
        .RequireRateLimiting("refresh");

        app.MapPost("/auth/register", async (
            RegisterRequest request,
            RegisterHandler handler,
            CancellationToken ct) =>
        {
            try
            {
                var response = await handler.HandleAsync(request, ct);
                return ApiEnvelope.Success(response);
            }
            catch (DuplicateEmailException)
            {
                return ApiEnvelope.Error(409, "DUPLICATE_EMAIL", "Email already registered");
            }
            catch (BadRequestException ex)
            {
                return ApiEnvelope.Error(400, "BAD_REQUEST", ex.Message);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
                when (ex.InnerException?.Message.Contains("23505") == true)
            {
                return ApiEnvelope.Error(409, "DUPLICATE_EMAIL", "Email already registered");
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
                when (ex.InnerException?.Message.Contains("23502") == true)
            {
                return ApiEnvelope.Error(400, "BAD_REQUEST", "A required field is missing");
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                return ApiEnvelope.Error(500, "INTERNAL_ERROR", "Could not complete registration");
            }
        })
        .RequireRateLimiting("register");
    }

    private static IResult AccountLockedResult(HttpContext httpContext, TimeSpan retryAfter)
    {
        var retryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
        httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        return ApiEnvelope.Error(429, "ACCOUNT_LOCKED", "Account is temporarily locked");
    }

    private static void SetRefreshTokenCookie(HttpContext httpContext, string rawRefreshToken)
    {
        httpContext.Response.Cookies.Append("refresh_token", rawRefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = TimeSpan.FromDays(7)
        });
    }

    private static void ExpireRefreshTokenCookie(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Append("refresh_token", string.Empty, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = TimeSpan.Zero
        });
    }
}
