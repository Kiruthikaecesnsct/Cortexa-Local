using Cortexa.Identity.Domain.Enums;

namespace Cortexa.Identity.Application.Interfaces;

public interface IAuditWriter
{
    Task LogAsync(
        AuditEventType eventType,
        string resourceType,
        string? resourceId,
        string action,
        object? details,
        CancellationToken ct);

    Task LogAuthAsync(
        AuditEventType eventType,
        Guid? subjectUserId,
        string action,
        bool success,
        string? failureReason,
        CancellationToken ct);
}
