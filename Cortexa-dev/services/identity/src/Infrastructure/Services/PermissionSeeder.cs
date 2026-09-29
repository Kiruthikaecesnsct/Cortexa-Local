using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Infrastructure.Configuration;
using Cortexa.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Cortexa.Identity.Infrastructure.Services;

public sealed class PermissionSeeder
{
    private const string PostgresUniqueViolationSqlState = "23505";

    private readonly IdentityDbContext _context;
    private readonly PermissionSeedSettings _settings;
    private readonly ILogger<PermissionSeeder> _logger;

    public PermissionSeeder(
        IdentityDbContext context,
        IOptions<PermissionSeedSettings> settings,
        ILogger<PermissionSeeder> logger)
    {
        _context = context;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct)
    {
        await SeedPermissionsAsync(ct);
        await SeedRolePermissionsAsync(ct);
    }

    private Task SeedPermissionsAsync(CancellationToken ct)
        => SeedMissingAsync("permissions", ComputeMissingPermissionsAsync, ct);

    private Task SeedRolePermissionsAsync(CancellationToken ct)
        => SeedMissingAsync("role permissions", ComputeMissingRolePermissionsAsync, ct);

    // Load existing names once, then materialize only the missing permission rows.
    // Bulk queries replace the per-entry round-trip loop that dominated cold-start
    // time and pushed the service past the CD revision-health gate.
    private async Task<List<object>> ComputeMissingPermissionsAsync(CancellationToken ct)
    {
        var existingNames = await _context.Permissions
            .AsNoTracking()
            .Select(p => p.Name)
            .ToListAsync(ct);
        var existing = new HashSet<string>(existingNames);

        return _settings.Catalog
            .Where(entry => !existing.Contains(entry.Name))
            .Select(entry => (object)Permission.Create(entry.Name, entry.Description))
            .ToList();
    }

    // One query for the name→id map, one for the existing (role, permission) pairs
    // — instead of two round-trips per pair.
    private async Task<List<object>> ComputeMissingRolePermissionsAsync(CancellationToken ct)
    {
        var permissionIdByName = await _context.Permissions
            .AsNoTracking()
            .ToDictionaryAsync(p => p.Name, p => p.Id, ct);

        var existingPairs = await _context.RolePermissions
            .AsNoTracking()
            .Select(rp => new { rp.Role, rp.PermissionId })
            .ToListAsync(ct);
        var existing = new HashSet<(Role, Guid)>(
            existingPairs.Select(rp => (rp.Role, rp.PermissionId)));

        return BuildMissingRolePermissions(permissionIdByName, existing);
    }

    private List<object> BuildMissingRolePermissions(
        IReadOnlyDictionary<string, Guid> permissionIdByName,
        HashSet<(Role, Guid)> existing)
    {
        var toAdd = new List<object>();

        foreach (var (roleString, permNames) in _settings.DefaultRolePermissions)
        {
            if (!Enum.TryParse<Role>(roleString, out var role))
            {
                _logger.LogWarning("Unknown role {Role} in DefaultRolePermissions. Skipping.", roleString);
                continue;
            }

            foreach (var permName in permNames)
            {
                if (!permissionIdByName.TryGetValue(permName, out var permissionId))
                {
                    _logger.LogWarning("Permission {Name} not found during role-permission seeding. Skipping.", permName);
                    continue;
                }

                if (existing.Add((role, permissionId)))
                    toAdd.Add(RolePermission.Create(role, permissionId));
            }
        }

        return toAdd;
    }

    // Insert the missing rows in one SaveChanges. Under a rolling deploy two
    // replicas can race; the loser hits the unique constraint. Because the batch
    // is transactional it rolls back whole, so we recompute the still-missing set
    // (now excluding the winner's rows) from the DB and retry. The missing set
    // strictly shrinks each round, guaranteeing convergence; the cap is a
    // belt-and-braces guard against an unexpected non-shrinking loop.
    private async Task SeedMissingAsync(
        string label,
        Func<CancellationToken, Task<List<object>>> computeMissing,
        CancellationToken ct)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var toAdd = await computeMissing(ct);
            if (toAdd.Count == 0)
                return;

            try
            {
                _context.AddRange(toAdd);
                await _context.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                _context.ChangeTracker.Clear();
                _logger.LogInformation(
                    "Concurrent instance seeded some {Label} (attempt {Attempt}/{Max}); recomputing missing rows.",
                    label, attempt, maxAttempts);
            }
        }

        throw new InvalidOperationException(
            $"Failed to seed {label} after {maxAttempts} attempts due to repeated concurrency conflicts.");
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException pg && pg.SqlState == PostgresUniqueViolationSqlState;
}
