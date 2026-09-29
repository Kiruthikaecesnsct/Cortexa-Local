using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;

namespace Cortexa.Identity.Application.Interfaces;

public sealed record AuditLogFilter(
    Guid? UserId,
    AuditEventType? EventType,
    DateTimeOffset? From,
    DateTimeOffset? To);

public sealed record AuditLogQueryResult(
    IReadOnlyList<AuditLog> Items,
    int Total);

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog auditLog, CancellationToken ct);

    Task<AuditLogQueryResult> QueryAsync(
        AuditLogFilter filter,
        int page,
        int size,
        CancellationToken ct);
}
