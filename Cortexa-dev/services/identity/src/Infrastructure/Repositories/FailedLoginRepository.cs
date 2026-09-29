using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Infrastructure.Configuration;
using Cortexa.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cortexa.Identity.Infrastructure.Repositories;

public sealed class FailedLoginRepository : IFailedLoginRepository
{
    private readonly IdentityDbContext _context;
    private readonly LockoutSettings _policy;

    public FailedLoginRepository(IdentityDbContext context, IOptions<LockoutSettings> policy)
    {
        _context = context;
        _policy = policy.Value;
    }

    public async Task<LockState> RegisterFailureAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var results = await _context.Database.SqlQuery<LockState>(
            $"""
             INSERT INTO failed_login_attempts
                 (id, user_id, attempt_count, lockout_count, window_started_at, last_attempt_at, locked_until)
             VALUES
                 (gen_random_uuid(), {userId}, 1, 0, {now}, {now}, NULL)
             ON CONFLICT (user_id) DO UPDATE SET
                 attempt_count = CASE
                     WHEN failed_login_attempts.last_attempt_at < {now} - make_interval(secs => {_policy.WindowSeconds})
                         THEN 1
                     ELSE failed_login_attempts.attempt_count + 1
                 END,
                 window_started_at = CASE
                     WHEN failed_login_attempts.last_attempt_at < {now} - make_interval(secs => {_policy.WindowSeconds})
                         THEN {now}
                     ELSE failed_login_attempts.window_started_at
                 END,
                 lockout_count = CASE
                     WHEN (CASE
                             WHEN failed_login_attempts.last_attempt_at < {now} - make_interval(secs => {_policy.WindowSeconds})
                                 THEN 1
                             ELSE failed_login_attempts.attempt_count + 1
                           END) >= {_policy.MaxAttempts}
                         THEN failed_login_attempts.lockout_count + 1
                     ELSE failed_login_attempts.lockout_count
                 END,
                 last_attempt_at = {now},
                 locked_until = CASE
                     WHEN (CASE
                             WHEN failed_login_attempts.last_attempt_at < {now} - make_interval(secs => {_policy.WindowSeconds})
                                 THEN 1
                             ELSE failed_login_attempts.attempt_count + 1
                           END) >= {_policy.MaxAttempts}
                         THEN {now} + make_interval(secs =>
                             LEAST(
                                 {_policy.BaseLockoutSeconds} * POWER(2, failed_login_attempts.lockout_count)::numeric,
                                 {_policy.MaxLockoutSeconds}::numeric
                             )::int)
                     ELSE failed_login_attempts.locked_until
                 END
             RETURNING attempt_count AS "AttemptCount", locked_until AS "LockedUntil"
             """)
            .ToListAsync(ct);

        return results[0];
    }

    public Task ResetAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE failed_login_attempts
             SET attempt_count = 0,
                 lockout_count = 0,
                 locked_until = NULL,
                 window_started_at = {now},
                 last_attempt_at = {now}
             WHERE user_id = {userId}
             """,
            ct);
    }

    public Task<FailedLoginAttempt?> GetAsync(Guid userId, CancellationToken ct)
        => _context.FailedLoginAttempts.AsNoTracking().FirstOrDefaultAsync(f => f.UserId == userId, ct);
}
