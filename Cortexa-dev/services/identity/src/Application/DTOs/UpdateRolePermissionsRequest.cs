namespace Cortexa.Identity.Application.DTOs;

public sealed record UpdateRolePermissionsRequest(IReadOnlyList<string> PermissionNames);
