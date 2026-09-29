using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using NSubstitute;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Handlers;

public sealed class ListAuditLogsHandlerTests
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ListAuditLogsHandler _handler;

    public ListAuditLogsHandlerTests()
    {
        _auditLogRepository = Substitute.For<IAuditLogRepository>();
        _handler = new ListAuditLogsHandler(_auditLogRepository);
    }

    [Fact]
    public async Task HandleAsync_FiltersByUserIdEventTypeAndDateRange_PassesFilterToRepository()
    {
        var userId = Guid.NewGuid();
        var from = DateTimeOffset.UtcNow.AddDays(-7);
        var to = DateTimeOffset.UtcNow;
        var request = new ListAuditLogsRequest(userId, AuditEventType.LoginFailed, from, to, 1, 20);

        _auditLogRepository
            .QueryAsync(Arg.Any<AuditLogFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AuditLogQueryResult(Array.Empty<AuditLog>(), 0));

        await _handler.HandleAsync(request, CancellationToken.None);

        await _auditLogRepository.Received(1).QueryAsync(
            Arg.Is<AuditLogFilter>(f =>
                f.UserId == userId
                && f.EventType == AuditEventType.LoginFailed
                && f.From == from
                && f.To == to),
            1,
            20,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PageBelowOne_ClampsToOne()
    {
        var request = new ListAuditLogsRequest(null, null, null, null, 0, 20);

        _auditLogRepository
            .QueryAsync(Arg.Any<AuditLogFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AuditLogQueryResult(Array.Empty<AuditLog>(), 0));

        var result = await _handler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(1, result.Page);
        await _auditLogRepository.Received(1).QueryAsync(
            Arg.Any<AuditLogFilter>(), 1, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SizeAboveMax_ClampsToMax()
    {
        var request = new ListAuditLogsRequest(null, null, null, null, 1, 500);

        _auditLogRepository
            .QueryAsync(Arg.Any<AuditLogFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AuditLogQueryResult(Array.Empty<AuditLog>(), 0));

        var result = await _handler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(100, result.Size);
    }

    [Fact]
    public async Task HandleAsync_SizeBelowOne_DefaultsToTwenty()
    {
        var request = new ListAuditLogsRequest(null, null, null, null, 1, 0);

        _auditLogRepository
            .QueryAsync(Arg.Any<AuditLogFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AuditLogQueryResult(Array.Empty<AuditLog>(), 0));

        var result = await _handler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(20, result.Size);
    }

    [Fact]
    public async Task HandleAsync_MapsEntitiesToDtosWithTotal()
    {
        var log = AuditLog.Create(Guid.NewGuid(), AuditEventType.UserCreated, "User", "r1", "create", "{}");
        var request = new ListAuditLogsRequest(null, null, null, null, 1, 20);

        _auditLogRepository
            .QueryAsync(Arg.Any<AuditLogFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AuditLogQueryResult(new[] { log }, 1));

        var result = await _handler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(1, result.Total);
        Assert.Single(result.Items);
        Assert.Equal(log.Id, result.Items[0].Id);
        Assert.Equal(log.EventType.ToString(), result.Items[0].EventType);
    }
}
