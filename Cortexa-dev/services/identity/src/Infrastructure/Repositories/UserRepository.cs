using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cortexa.Identity.Infrastructure.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly IdentityDbContext _context;

    public UserRepository(IdentityDbContext context)
    {
        _context = context;
    }

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct)
        => _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<User?> GetTrackedByEmailAsync(string email, CancellationToken ct)
        => _context.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct)
        => _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task AddAsync(User user, CancellationToken ct)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<bool> PromoteToSuperAdminAsync(
        string fromEmail,
        string superAdminEmail,
        string superAdminUsername,
        CancellationToken ct)
    {
        var rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE users
             SET email = {superAdminEmail},
                 username = {superAdminUsername},
                 role = 'SuperAdmin',
                 is_system = true
             WHERE email = {fromEmail}
               AND is_system = false
             """,
            ct);

        return rowsAffected > 0;
    }

    public async Task<IReadOnlyList<User>> ListByOrgAsync(Guid orgId, CancellationToken ct)
    {
        var users = await _context.Users
            .AsNoTracking()
            .Where(u => u.OrganizationId == orgId)
            .ToListAsync(ct);
        return users.AsReadOnly();
    }

    public Task<User?> GetByIdInOrgAsync(Guid id, Guid orgId, CancellationToken ct)
        => _context.Users.FirstOrDefaultAsync(u => u.Id == id && u.OrganizationId == orgId, ct);

    public Task<bool> EmailExistsInOrgAsync(string email, Guid orgId, CancellationToken ct)
        => _context.Users.AnyAsync(u => u.Email == email && u.OrganizationId == orgId, ct);

    public Task<bool> UsernameExistsAsync(string username, CancellationToken ct)
        => _context.Users.AnyAsync(u => u.Username == username, ct);

    public Task<int> CountActiveAdminsInOrgAsync(Guid orgId, Guid excludeUserId, CancellationToken ct)
        => _context.Users.CountAsync(
            u => u.OrganizationId == orgId
                 && u.Role == Role.Admin
                 && u.IsEnabled
                 && u.Id != excludeUserId,
            ct);

    public Task<User?> GetTrackedByIdAsync(Guid id, CancellationToken ct)
        => _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<bool> EmailExistsInOrgExcludingUserAsync(string email, Guid orgId, Guid excludeUserId, CancellationToken ct)
        => _context.Users.AnyAsync(
            u => u.Email == email && u.OrganizationId == orgId && u.Id != excludeUserId,
            ct);

    public Task<bool> UsernameExistsExcludingUserAsync(string username, Guid excludeUserId, CancellationToken ct)
        => _context.Users.AnyAsync(u => u.Username == username && u.Id != excludeUserId, ct);

    public async Task StageRotateStampByRoleAsync(Role role, CancellationToken ct)
    {
        var users = await _context.Users.Where(u => u.Role == role).ToListAsync(ct);
        foreach (var user in users)
            user.InvalidateSessions();
    }

    public Task SaveChangesAsync(CancellationToken ct)
        => _context.SaveChangesAsync(ct);
}
