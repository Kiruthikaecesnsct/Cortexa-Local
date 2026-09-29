using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;

namespace Cortexa.Identity.Application.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct);
    Task<User?> GetTrackedByEmailAsync(string email, CancellationToken ct);
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(User user, CancellationToken ct);
    Task<bool> PromoteToSuperAdminAsync(
        string fromEmail,
        string superAdminEmail,
        string superAdminUsername,
        CancellationToken ct);
    Task<IReadOnlyList<User>> ListByOrgAsync(Guid orgId, CancellationToken ct);
    Task<User?> GetByIdInOrgAsync(Guid id, Guid orgId, CancellationToken ct);
    Task<bool> EmailExistsInOrgAsync(string email, Guid orgId, CancellationToken ct);
    Task<bool> UsernameExistsAsync(string username, CancellationToken ct);
    Task<int> CountActiveAdminsInOrgAsync(Guid orgId, Guid excludeUserId, CancellationToken ct);
    Task<User?> GetTrackedByIdAsync(Guid id, CancellationToken ct);
    Task<bool> EmailExistsInOrgExcludingUserAsync(string email, Guid orgId, Guid excludeUserId, CancellationToken ct);
    Task<bool> UsernameExistsExcludingUserAsync(string username, Guid excludeUserId, CancellationToken ct);
    Task StageRotateStampByRoleAsync(Role role, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
