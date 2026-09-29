using Cortexa.Identity.Domain.Enums;

namespace Cortexa.Identity.Application.DTOs;

public sealed record ListAuditLogsRequest(
    Guid? UserId,
    AuditEventType? EventType,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page,
    int Size);
