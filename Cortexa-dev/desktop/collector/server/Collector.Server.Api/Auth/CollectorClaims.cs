using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using Collector.Server.Application.Upload;

namespace Collector.Server.Api.Auth;

public static class CollectorClaims
{
    public const string Subject = "sub";
    public const string OrgId = "org_id";
    public const string Stamp = "stamp";
    public const string Permissions = "perms";
    public const string JobsSubmit = "jobs:submit";

    private const char JsonArrayStart = '[';

    public static bool TryGetCaller(ClaimsPrincipal principal, [NotNullWhen(true)] out UploadCaller? caller)
    {
        var userId = principal.FindFirstValue(Subject);
        var orgId = principal.FindFirstValue(OrgId);
        caller = string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(orgId)
            ? null
            : new UploadCaller(userId, orgId);
        return caller is not null;
    }

    public static bool TryGetStamp(ClaimsPrincipal principal, out Guid stamp)
    {
        stamp = Guid.Empty;
        return Guid.TryParse(principal.FindFirstValue(Stamp), out stamp);
    }

    public static bool HasPermission(ClaimsPrincipal principal, string permission) =>
        principal.FindAll(Permissions)
            .SelectMany(claim => ExpandValues(claim.Value))
            .Contains(permission, StringComparer.Ordinal);

    private static IEnumerable<string> ExpandValues(string value)
    {
        if (!value.TrimStart().StartsWith(JsonArrayStart))
        {
            return [value];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(value) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
