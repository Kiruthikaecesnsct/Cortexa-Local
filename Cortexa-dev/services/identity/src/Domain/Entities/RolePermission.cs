using Cortexa.Identity.Domain.Enums;

namespace Cortexa.Identity.Domain.Entities;

public sealed class RolePermission
{
    private RolePermission() { }

    public Role Role { get; private set; }
    public Guid PermissionId { get; private set; }
    public Permission Permission { get; private set; } = null!;

    public static RolePermission Create(Role role, Guid permissionId) => new()
    {
        Role = role,
        PermissionId = permissionId
    };
}
