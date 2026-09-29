using System.Security.Claims;

namespace Cortexa.JobOrchestrator.Api.Auth;

public sealed record CallerContext(string? OrgId, string UserId, bool IsSuperAdmin);

public sealed record CallerContextResult(CallerContext? Context, string? Error)
{
    public bool IsSuccess => Context is not null;

    public static CallerContextResult Success(CallerContext context) => new(context, null);

    public static CallerContextResult Failure(string error) => new(null, error);
}

public static class CallerContextResolver
{
    private const string OrgIdHeader = "X-Org-Id";
    private const string UserIdHeader = "X-User-Id";
    private const string SuperAdminRole = "SuperAdmin";

    public static CallerContextResult Resolve(HttpContext ctx)
    {
        var orgId = ctx.Request.Headers[OrgIdHeader].ToString();
        var isSuperAdmin = ctx.User.IsInRole(SuperAdminRole);
        var userId = ResolveUserId(ctx);

        if (string.IsNullOrWhiteSpace(orgId) && isSuperAdmin)
            return CallerContextResult.Success(new CallerContext(null, userId, true));

        if (string.IsNullOrWhiteSpace(orgId))
            return CallerContextResult.Failure("Missing or blank X-Org-Id header.");

        return CallerContextResult.Success(new CallerContext(orgId, userId, isSuperAdmin));
    }

    private static string ResolveUserId(HttpContext ctx)
    {
        var headerUserId = ctx.Request.Headers[UserIdHeader].ToString();
        if (!string.IsNullOrWhiteSpace(headerUserId))
            return headerUserId;

        return ctx.User.Identity?.Name
            ?? ctx.User.FindFirst("sub")?.Value
            ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "unknown";
    }
}
