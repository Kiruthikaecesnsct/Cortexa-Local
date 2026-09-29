using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;

namespace Cortexa.Identity.Application.Interfaces;

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token, CancellationToken ct);
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
    Task StageRevokeAllActiveByRoleAsync(Role role, CancellationToken ct);
    Task StageRevokeAllForUserAsync(Guid userId, CancellationToken ct);
}
