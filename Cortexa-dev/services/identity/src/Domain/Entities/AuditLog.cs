using Cortexa.Identity.Domain.Enums;

namespace Cortexa.Identity.Domain.Entities;

public sealed class AuditLog
{
    private AuditLog() { }

    public Guid Id { get; private set; }
    public Guid? UserId { get; private set; }
    public AuditEventType EventType { get; private set; }
    public string ResourceType { get; private set; } = string.Empty;
    public string? ResourceId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string? Details { get; private set; }
    public DateTimeOffset CreatedDate { get; private set; }

    public static AuditLog Create(
        Guid? actorUserId,
        AuditEventType eventType,
        string resourceType,
        string? resourceId,
        string action,
        string? detailsJson)
    {
        return new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = actorUserId,
            EventType = eventType,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Action = action,
            Details = detailsJson,
            CreatedDate = DateTimeOffset.UtcNow
        };
    }
}
