using Cortexa.Identity.Domain.Entities;

namespace Cortexa.Identity.Application.Interfaces;

public sealed record LockState(int AttemptCount, DateTimeOffset? LockedUntil);

public interface IFailedLoginRepository
{
    Task<LockState> RegisterFailureAsync(Guid userId, CancellationToken ct);
    Task ResetAsync(Guid userId, CancellationToken ct);
    Task<FailedLoginAttempt?> GetAsync(Guid userId, CancellationToken ct);
}
