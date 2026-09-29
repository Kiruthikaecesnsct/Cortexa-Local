namespace Cortexa.Identity.Application.DTOs;

public sealed record AuditLogDto(
    Guid Id,
    Guid? UserId,
    string EventType,
    string ResourceType,
    string? ResourceId,
    string Action,
    string? Details,
    DateTimeOffset CreatedDate);
