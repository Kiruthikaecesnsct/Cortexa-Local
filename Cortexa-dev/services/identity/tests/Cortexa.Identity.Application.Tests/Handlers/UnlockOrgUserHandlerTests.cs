using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Application.Tests.Helpers;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;
using NSubstitute;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Handlers;

public sealed class UnlockOrgUserHandlerTests
{
    private readonly IUserRepository _userRepo;
    private readonly IFailedLoginRepository _failedLoginRepo;
    private readonly IAuditWriter _auditWriter;
    private readonly UnlockOrgUserHandler _handler;

    public UnlockOrgUserHandlerTests()
    {
        _userRepo = Substitute.For<IUserRepository>();
        _failedLoginRepo = Substitute.For<IFailedLoginRepository>();
        _auditWriter = Substitute.For<IAuditWriter>();
        _handler = new UnlockOrgUserHandler(_userRepo, _failedLoginRepo, _auditWriter);
    }

    [Fact]
    public async Task HandleAsync_UserNotInOrg_ThrowsForbiddenException()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        _userRepo.GetByIdInOrgAsync(userId, orgId, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _handler.HandleAsync(userId, orgId, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ValidUser_ResetsFailedLoginCounter()
    {
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(role: Role.Researcher, organizationId: orgId);

        _userRepo.GetByIdInOrgAsync(user.Id, orgId, Arg.Any<CancellationToken>())
            .Returns(user);

        await _handler.HandleAsync(user.Id, orgId, CancellationToken.None);

        await _failedLoginRepo.Received(1).ResetAsync(user.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidUser_LogsUserUnlocked()
    {
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(role: Role.Researcher, organizationId: orgId);

        _userRepo.GetByIdInOrgAsync(user.Id, orgId, Arg.Any<CancellationToken>())
            .Returns(user);

        await _handler.HandleAsync(user.Id, orgId, CancellationToken.None);

        await _auditWriter.Received(1).LogAsync(
            AuditEventType.UserUnlocked,
            "User",
            user.Id.ToString(),
            "unlock-org-user",
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }
}
