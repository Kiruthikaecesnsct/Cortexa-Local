using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;

namespace Cortexa.Identity.Application.Interfaces;

public interface IPermissionRepository
{
    Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken ct);
    Task<IReadOnlyList<Permission>> GetByRoleAsync(Role role, CancellationToken ct);
    Task<bool> AllExistAsync(IEnumerable<string> names, CancellationToken ct);
    Task StageRolePermissionsReplaceAsync(Role role, IReadOnlyList<Permission> permissions, CancellationToken ct);
    Task<IReadOnlyList<string>> GetPermissionNamesForUserAsync(Role role, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
