using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Domain.Exceptions;

namespace Cortexa.Identity.Api.Endpoints;

public static class AdminPermissionEndpoints
{
    public static void MapAdminPermissionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin")
            .RequireAuthorization("SuperAdminOnly");

        group.MapGet("/permissions", async (
            GetPermissionsHandler h,
            CancellationToken ct) =>
        {
            var result = await h.HandleAsync(ct);
            return ApiEnvelope.Success(result);
        });

        group.MapGet("/roles/{roleId}/permissions", async (
            string roleId,
            GetRolePermissionsHandler h,
            CancellationToken ct) =>
        {
            try
            {
                var result = await h.HandleAsync(roleId, ct);
                return ApiEnvelope.Success(result);
            }
            catch (BadRequestException ex)
            {
                return ApiEnvelope.Error(400, "BAD_REQUEST", ex.Message);
            }
        });

        group.MapPut("/roles/{roleId}/permissions", async (
            string roleId,
            UpdateRolePermissionsRequest req,
            UpdateRolePermissionsHandler h,
            CancellationToken ct) =>
        {
            try
            {
                await h.HandleAsync(roleId, req, ct);
                return Results.NoContent();
            }
            catch (BadRequestException ex)
            {
                return ApiEnvelope.Error(400, "BAD_REQUEST", ex.Message);
            }
            catch (ForbiddenException ex)
            {
                return ApiEnvelope.Error(403, "FORBIDDEN", ex.Message);
            }
        });
    }
}
