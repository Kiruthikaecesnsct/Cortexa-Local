using Cortexa.Identity.Application.Auditing;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Cortexa.Identity.Infrastructure.Services;

public sealed class AuditWriter : IAuditWriter
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ICurrentActor _currentActor;
    private readonly ILogger<AuditWriter> _logger;

    public AuditWriter(
        IAuditLogRepository auditLogRepository,
        ICurrentActor currentActor,
        ILogger<AuditWriter> logger)
    {
        _auditLogRepository = auditLogRepository;
        _currentActor = currentActor;
        _logger = logger;
    }

    public async Task LogAsync(
        AuditEventType eventType,
        string resourceType,
        string? resourceId,
        string action,
        object? details,
        CancellationToken ct)
    {
        await WriteAsync(_currentActor.UserId, eventType, resourceType, resourceId, action, details, ct);
    }

    public async Task LogAuthAsync(
        AuditEventType eventType,
        Guid? subjectUserId,
        string action,
        bool success,
        string? failureReason,
        CancellationToken ct)
    {
        var details = new { success, failureReason };
        await WriteAsync(subjectUserId, eventType, "Auth", subjectUserId?.ToString(), action, details, ct);
    }

    private async Task WriteAsync(
        Guid? auditUserId,
        AuditEventType eventType,
        string resourceType,
        string? resourceId,
        string action,
        object? details,
        CancellationToken ct)
    {
        try
        {
            var detailsJson = AuditDetailSerializer.Serialize(details);
            var auditLog = AuditLog.Create(auditUserId, eventType, resourceType, resourceId, action, detailsJson);
            await _auditLogRepository.AddAsync(auditLog, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to write audit log entry for event {EventType} on resource {ResourceType}/{ResourceId}.",
                eventType,
                resourceType,
                resourceId);
        }
    }
}
