using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cortexa.Identity.Infrastructure.Repositories;

public sealed class PermissionRepository : IPermissionRepository
{
    private readonly IdentityDbContext _context;

    public PermissionRepository(IdentityDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken ct)
        => await _context.Permissions.AsNoTracking().ToListAsync(ct);

    public async Task<IReadOnlyList<Permission>> GetByRoleAsync(Role role, CancellationToken ct)
        => await _context.RolePermissions
            .AsNoTracking()
            .Where(rp => rp.Role == role)
            .Select(rp => rp.Permission)
            .ToListAsync(ct);

    public async Task<bool> AllExistAsync(IEnumerable<string> names, CancellationToken ct)
    {
        var nameList = names.Distinct().ToList();
        if (nameList.Count == 0)
            return true;

        var count = await _context.Permissions
            .AsNoTracking()
            .CountAsync(p => nameList.Contains(p.Name), ct);

        return count == nameList.Count;
    }

    public async Task StageRolePermissionsReplaceAsync(Role role, IReadOnlyList<Permission> permissions, CancellationToken ct)
    {
        var existing = await _context.RolePermissions
            .Where(rp => rp.Role == role)
            .ToListAsync(ct);

        _context.RolePermissions.RemoveRange(existing);

        foreach (var perm in permissions)
            _context.RolePermissions.Add(RolePermission.Create(role, perm.Id));
    }

    public Task SaveChangesAsync(CancellationToken ct)
        => _context.SaveChangesAsync(ct);

    public async Task<IReadOnlyList<string>> GetPermissionNamesForUserAsync(Role role, CancellationToken ct)
        => await _context.RolePermissions
            .AsNoTracking()
            .Where(rp => rp.Role == role)
            .Select(rp => rp.Permission.Name)
            .ToListAsync(ct);
}
