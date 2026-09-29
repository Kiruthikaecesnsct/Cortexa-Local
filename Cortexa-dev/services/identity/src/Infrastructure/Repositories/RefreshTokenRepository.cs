using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cortexa.Identity.Infrastructure.Repositories;

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly IdentityDbContext _context;

    public RefreshTokenRepository(IdentityDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(RefreshToken token, CancellationToken ct)
    {
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync(ct);
    }

    public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct)
        => _context.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, ct);

    public Task SaveChangesAsync(CancellationToken ct)
        => _context.SaveChangesAsync(ct);

    public async Task StageRevokeAllActiveByRoleAsync(Role role, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var userIds = await _context.Users
            .Where(u => u.Role == role)
            .Select(u => u.Id)
            .ToListAsync(ct);

        var tokens = await _context.RefreshTokens
            .Where(rt => userIds.Contains(rt.UserId)
                      && rt.UsedAt == null
                      && !rt.Revoked
                      && rt.ExpiresAt > now)
            .ToListAsync(ct);

        foreach (var token in tokens)
            token.Revoke();
    }

    public async Task StageRevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var tokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId
                      && rt.UsedAt == null
                      && !rt.Revoked
                      && rt.ExpiresAt > now)
            .ToListAsync(ct);

        foreach (var token in tokens)
            token.Revoke();
    }
}
