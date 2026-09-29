namespace Cortexa.Identity.Application.DTOs;

public sealed record RolePermissionsResponse(string Role, IReadOnlyList<PermissionDto> Permissions);
