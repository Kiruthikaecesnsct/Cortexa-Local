using System.Security.Claims;
using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Domain.Exceptions;

namespace Cortexa.Identity.Api.Endpoints;

public static class AccountEndpoints
{
    public static WebApplication MapAccountEndpoints(this WebApplication app)
    {
        app.MapPatch("/account/profile", UpdateProfileAsync).RequireAuthorization();
        app.MapPatch("/account/password", ChangePasswordAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> UpdateProfileAsync(
        UpdateProfileRequest req,
        UpdateProfileHandler h,
        HttpContext ctx,
        CancellationToken ct)
    {
        try
        {
            var userId = ResolveUserId(ctx);
            var orgId = ResolveOrgId(ctx);
            var result = await h.HandleAsync(userId, orgId, req, ct);
            return ApiEnvelope.Success(result);
        }
        catch (ForbiddenException ex)
        {
            return ApiEnvelope.Error(403, "FORBIDDEN", ex.Message);
        }
        catch (BadRequestException ex)
        {
            return ApiEnvelope.Error(400, "BAD_REQUEST", ex.Message);
        }
        catch (ImmutableUserException ex)
        {
            return ApiEnvelope.Error(403, "IMMUTABLE_USER", ex.Message);
        }
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest req,
        ChangePasswordHandler h,
        HttpContext ctx,
        CancellationToken ct)
    {
        try
        {
            var userId = ResolveUserId(ctx);
            await h.HandleAsync(userId, req, ct);
            return Results.NoContent();
        }
        catch (UnauthorizedException)
        {
            return ApiEnvelope.Error(400, "INVALID_CURRENT_PASSWORD", "Current password is incorrect");
        }
        catch (ForbiddenException ex)
        {
            return ApiEnvelope.Error(403, "FORBIDDEN", ex.Message);
        }
        catch (BadRequestException ex)
        {
            return ApiEnvelope.Error(400, "BAD_REQUEST", ex.Message);
        }
        catch (ImmutableUserException ex)
        {
            return ApiEnvelope.Error(403, "IMMUTABLE_USER", ex.Message);
        }
    }

    private static Guid ResolveUserId(HttpContext ctx)
    {
        var raw = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(raw, out var userId))
            throw new ForbiddenException("Access denied.");
        return userId;
    }

    private static Guid ResolveOrgId(HttpContext ctx)
    {
        var raw = ctx.User.FindFirst("org_id")?.Value;
        if (!Guid.TryParse(raw, out var orgId))
            throw new ForbiddenException("Access denied.");
        return orgId;
    }
}
