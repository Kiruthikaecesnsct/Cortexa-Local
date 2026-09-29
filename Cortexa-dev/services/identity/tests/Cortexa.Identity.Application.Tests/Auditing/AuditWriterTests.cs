using Cortexa.Identity.Application.Auditing;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Auditing;

public sealed class AuditWriterTests
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ICurrentActor _currentActor;
    private readonly ILogger<AuditWriter> _logger;
    private readonly AuditWriter _auditWriter;

    public AuditWriterTests()
    {
        _auditLogRepository = Substitute.For<IAuditLogRepository>();
        _currentActor = Substitute.For<ICurrentActor>();
        _logger = Substitute.For<ILogger<AuditWriter>>();
        _auditWriter = new AuditWriter(_auditLogRepository, _currentActor, _logger);
    }

    [Fact]
    public async Task LogAsync_DetailsContainSensitiveFields_RedactsBeforePersisting()
    {
        var actorId = Guid.NewGuid();
        _currentActor.UserId.Returns(actorId);

        AuditLog? captured = null;
        await _auditLogRepository.AddAsync(
            Arg.Do<AuditLog>(a => captured = a),
            Arg.Any<CancellationToken>());

        await _auditWriter.LogAsync(
            AuditEventType.UserUpdated,
            "User",
            Guid.NewGuid().ToString(),
            "update-profile",
            new { email = "someone@example.com", passwordHash = "abc123", changedFields = new[] { "Email" } },
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.DoesNotContain("someone@example.com", captured!.Details);
        Assert.DoesNotContain("abc123", captured.Details);
        Assert.Contains("REDACTED", captured.Details);
        Assert.Contains("Email", captured.Details);
    }

    [Fact]
    public async Task LogAsync_UsesCurrentActorUserId()
    {
        var actorId = Guid.NewGuid();
        _currentActor.UserId.Returns(actorId);

        AuditLog? captured = null;
        await _auditLogRepository.AddAsync(
            Arg.Do<AuditLog>(a => captured = a),
            Arg.Any<CancellationToken>());

        await _auditWriter.LogAsync(
            AuditEventType.UserCreated, "User", Guid.NewGuid().ToString(), "create", null, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(actorId, captured!.UserId);
    }

    [Fact]
    public async Task LogAsync_RepositoryThrows_DoesNotPropagateException()
    {
        _currentActor.UserId.Returns((Guid?)null);
        _auditLogRepository
            .AddAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("db unavailable"));

        var exception = await Record.ExceptionAsync(() => _auditWriter.LogAsync(
            AuditEventType.UserCreated, "User", Guid.NewGuid().ToString(), "create", null, CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task LogAuthAsync_RepositoryThrows_DoesNotPropagateException()
    {
        _auditLogRepository
            .AddAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("db unavailable"));

        var exception = await Record.ExceptionAsync(() => _auditWriter.LogAuthAsync(
            AuditEventType.LoginFailed, null, "login", success: false, "wrong-password", CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task LogAuthAsync_SetsUserIdFromSubjectNotCurrentActor()
    {
        var subjectId = Guid.NewGuid();
        _currentActor.UserId.Returns((Guid?)null);

        AuditLog? captured = null;
        await _auditLogRepository.AddAsync(
            Arg.Do<AuditLog>(a => captured = a),
            Arg.Any<CancellationToken>());

        await _auditWriter.LogAuthAsync(
            AuditEventType.LoginSucceeded, subjectId, "login", success: true, failureReason: null, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(subjectId, captured!.UserId);
        Assert.Equal("Auth", captured.ResourceType);
    }

    [Fact]
    public void AuditDetailSerializer_NullDetails_ReturnsNull()
    {
        var result = AuditDetailSerializer.Serialize(null);
        Assert.Null(result);
    }

    [Fact]
    public void AuditDetailSerializer_NestedSensitiveField_IsRedacted()
    {
        var details = new
        {
            user = new { email = "nested@example.com", refreshToken = "raw-token-value" }
        };

        var json = AuditDetailSerializer.Serialize(details);

        Assert.NotNull(json);
        Assert.DoesNotContain("nested@example.com", json);
        Assert.DoesNotContain("raw-token-value", json);
    }
}
