using System.Security.Cryptography;
using System.Text;
using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Cortexa.Identity.Api.Endpoints;

public static class InternalEndpoints
{
    public static WebApplication MapInternalEndpoints(this WebApplication app)
    {
        app.MapGet("/internal/users/{id:guid}/status", GetUserStatusAsync);
        return app;
    }

    private static async Task<IResult> GetUserStatusAsync(
        Guid id,
        HttpContext ctx,
        IUserRepository userRepo,
        IOptions<InternalApiSettings> settings,
        CancellationToken ct)
    {
        if (!IsAuthorized(ctx, settings.Value))
            return Results.Unauthorized();

        var user = await userRepo.GetByIdAsync(id, ct);
        if (user is null)
            return Results.NotFound();

        return Results.Ok(new InternalUserStatusResponse(user.IsEnabled, user.SecurityStamp.ToString()));
    }

    private static bool IsAuthorized(HttpContext ctx, InternalApiSettings settings)
    {
        if (string.IsNullOrEmpty(settings.SharedSecret) || string.IsNullOrEmpty(settings.HeaderName))
            return false;

        var provided = ctx.Request.Headers[settings.HeaderName].ToString();
        if (string.IsNullOrEmpty(provided))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(settings.SharedSecret));
    }
}
