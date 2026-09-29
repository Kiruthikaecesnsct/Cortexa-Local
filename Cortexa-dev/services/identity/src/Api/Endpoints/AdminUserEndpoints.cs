using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Domain.Exceptions;

namespace Cortexa.Identity.Api.Endpoints;

public static class AdminUserEndpoints
{
    public static WebApplication MapAdminUserEndpoints(this WebApplication app)
    {
        app.MapPost("/admin/users", CreateUserAsync).RequireAuthorization("AdminOnly");
        app.MapGet("/admin/users", ListUsersAsync).RequireAuthorization("AdminOnly");
        app.MapGet("/admin/users/{id:guid}", GetUserAsync).RequireAuthorization("AdminOnly");
        app.MapPatch("/admin/users/{id:guid}/disable", DisableUserAsync).RequireAuthorization("AdminOnly");
        app.MapPatch("/admin/users/{id:guid}/enable", EnableUserAsync).RequireAuthorization("AdminOnly");
        app.MapPatch("/admin/users/{id:guid}/role", ChangeRoleAsync).RequireAuthorization("AdminOnly");
        app.MapPost("/admin/orgs/{orgId:guid}/users/{id:guid}/unlock", UnlockUserAsync)
            .RequireAuthorization("AdminOrSuperAdmin");
        return app;
    }

    private static async Task<IResult> CreateUserAsync(
        CreateUserRequest req,
        CreateOrgUserHandler h,
        HttpContext ctx,
        CancellationToken ct)
    {
        try
        {
            var orgId = ResolveOrgId(ctx);
            var result = await h.HandleAsync(req, orgId, ct);
            return Results.Created($"/admin/users/{result.Id}", new { success = true, data = result });
        }
        catch (ForbiddenException ex)
        {
            return ApiEnvelope.Error(403, "FORBIDDEN", ex.Message);
        }
        catch (BadRequestException ex)
        {
            return ApiEnvelope.Error(400, "BAD_REQUEST", ex.Message);
        }
    }

    private static async Task<IResult> ListUsersAsync(
        ListOrgUsersHandler h,
        HttpContext ctx,
        CancellationToken ct)
    {
        try
        {
            var orgId = ResolveOrgId(ctx);
            var result = await h.HandleAsync(orgId, ct);
            return ApiEnvelope.Success(result);
        }
        catch (ForbiddenException ex)
        {
            return ApiEnvelope.Error(403, "FORBIDDEN", ex.Message);
        }
    }

    private static async Task<IResult> GetUserAsync(
        Guid id,
        GetOrgUserHandler h,
        HttpContext ctx,
        CancellationToken ct)
    {
        try
        {
            var orgId = ResolveOrgId(ctx);
            var result = await h.HandleAsync(id, orgId, ct);
            return ApiEnvelope.Success(result);
        }
        catch (ForbiddenException ex)
        {
            return ApiEnvelope.Error(403, "FORBIDDEN", ex.Message);
        }
    }

    private static async Task<IResult> DisableUserAsync(
        Guid id,
        DisableOrgUserHandler h,
        HttpContext ctx,
        CancellationToken ct)
    {
        try
        {
            var orgId = ResolveOrgId(ctx);
            await h.HandleAsync(id, orgId, ct);
            return Results.NoContent();
        }
        catch (ForbiddenException ex)
        {
            return ApiEnvelope.Error(403, "FORBIDDEN", ex.Message);
        }
        catch (BadRequestException ex)
        {
            return ApiEnvelope.Error(400, "BAD_REQUEST", ex.Message);
        }
    }

    private static async Task<IResult> EnableUserAsync(
        Guid id,
        EnableOrgUserHandler h,
        HttpContext ctx,
        CancellationToken ct)
    {
        try
        {
            var orgId = ResolveOrgId(ctx);
            await h.HandleAsync(id, orgId, ct);
            return Results.NoContent();
        }
        catch (ForbiddenException ex)
        {
            return ApiEnvelope.Error(403, "FORBIDDEN", ex.Message);
        }
    }

    private static async Task<IResult> ChangeRoleAsync(
        Guid id,
        ChangeRoleRequest req,
        ChangeOrgUserRoleHandler h,
        HttpContext ctx,
        CancellationToken ct)
    {
        try
        {
            var orgId = ResolveOrgId(ctx);
            await h.HandleAsync(id, orgId, req, ct);
            return Results.NoContent();
        }
        catch (ForbiddenException ex)
        {
            return ApiEnvelope.Error(403, "FORBIDDEN", ex.Message);
        }
        catch (BadRequestException ex)
        {
            return ApiEnvelope.Error(400, "BAD_REQUEST", ex.Message);
        }
    }

    private static async Task<IResult> UnlockUserAsync(
        Guid orgId,
        Guid id,
        UnlockOrgUserHandler h,
        HttpContext ctx,
        CancellationToken ct)
    {
        try
        {
            EnsureOrgAccess(ctx, orgId);
            await h.HandleAsync(id, orgId, ct);
            return Results.NoContent();
        }
        catch (ForbiddenException ex)
        {
            return ApiEnvelope.Error(403, "FORBIDDEN", ex.Message);
        }
    }

    private static Guid ResolveOrgId(HttpContext ctx)
    {
        var raw = ctx.User.FindFirst("org_id")?.Value;
        if (!Guid.TryParse(raw, out var orgId))
            throw new ForbiddenException("Access denied.");
        return orgId;
    }

    private static void EnsureOrgAccess(HttpContext ctx, Guid orgId)
    {
        if (ctx.User.FindFirst("role")?.Value == "SuperAdmin")
            return;

        var raw = ctx.User.FindFirst("org_id")?.Value;
        if (!Guid.TryParse(raw, out var callerOrgId) || callerOrgId != orgId)
            throw new ForbiddenException("Access denied.");
    }
}
