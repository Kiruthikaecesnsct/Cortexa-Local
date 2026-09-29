using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Application.Tests.Helpers;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;
using NSubstitute;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Handlers;

public sealed class DisableOrgUserHandlerTests
{
    private readonly IUserRepository _userRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IAuditWriter _auditWriter;
    private readonly DisableOrgUserHandler _handler;

    public DisableOrgUserHandlerTests()
    {
        _userRepo = Substitute.For<IUserRepository>();
        _refreshTokenRepo = Substitute.For<IRefreshTokenRepository>();
        _auditWriter = Substitute.For<IAuditWriter>();
        _handler = new DisableOrgUserHandler(_userRepo, _refreshTokenRepo, _auditWriter);
    }

    [Fact]
    public async Task HandleAsync_UserNotInOrg_ThrowsForbiddenException()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        _userRepo.GetByIdInOrgAsync(userId, orgId, Arg.Any<CancellationToken>())
            .Returns((Domain.Entities.User?)null);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _handler.HandleAsync(userId, orgId, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_LastActiveAdmin_ThrowsBadRequestException()
    {
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(role: Role.Admin, isEnabled: true, organizationId: orgId);

        _userRepo.GetByIdInOrgAsync(user.Id, orgId, Arg.Any<CancellationToken>())
            .Returns(user);
        _userRepo.CountActiveAdminsInOrgAsync(orgId, user.Id, Arg.Any<CancellationToken>())
            .Returns(0);

        await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(user.Id, orgId, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_AdminWithRemainingAdmins_CallsSaveChangesOnce()
    {
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(role: Role.Admin, isEnabled: true, organizationId: orgId);

        _userRepo.GetByIdInOrgAsync(user.Id, orgId, Arg.Any<CancellationToken>())
            .Returns(user);
        _userRepo.CountActiveAdminsInOrgAsync(orgId, user.Id, Arg.Any<CancellationToken>())
            .Returns(1);

        await _handler.HandleAsync(user.Id, orgId, CancellationToken.None);

        await _userRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ResearcherUser_DoesNotCheckAdminCount()
    {
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(role: Role.Researcher, isEnabled: true, organizationId: orgId);

        _userRepo.GetByIdInOrgAsync(user.Id, orgId, Arg.Any<CancellationToken>())
            .Returns(user);

        await _handler.HandleAsync(user.Id, orgId, CancellationToken.None);

        await _userRepo.DidNotReceive().CountActiveAdminsInOrgAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ResearcherUser_CallsSaveChangesOnce()
    {
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(role: Role.Researcher, isEnabled: true, organizationId: orgId);

        _userRepo.GetByIdInOrgAsync(user.Id, orgId, Arg.Any<CancellationToken>())
            .Returns(user);

        await _handler.HandleAsync(user.Id, orgId, CancellationToken.None);

        await _userRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SuccessfulDisable_SetsUserIsEnabledFalse()
    {
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(role: Role.Researcher, isEnabled: true, organizationId: orgId);

        _userRepo.GetByIdInOrgAsync(user.Id, orgId, Arg.Any<CancellationToken>())
            .Returns(user);

        await _handler.HandleAsync(user.Id, orgId, CancellationToken.None);

        Assert.False(user.IsEnabled);
    }

    [Fact]
    public async Task HandleAsync_SuccessfulDisable_RevokesAllRefreshTokensForUser()
    {
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(role: Role.Researcher, isEnabled: true, organizationId: orgId);

        _userRepo.GetByIdInOrgAsync(user.Id, orgId, Arg.Any<CancellationToken>())
            .Returns(user);

        await _handler.HandleAsync(user.Id, orgId, CancellationToken.None);

        await _refreshTokenRepo.Received(1).StageRevokeAllForUserAsync(user.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SuccessfulDisable_RotatesSecurityStamp()
    {
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(role: Role.Researcher, isEnabled: true, organizationId: orgId);
        var previousStamp = user.SecurityStamp;

        _userRepo.GetByIdInOrgAsync(user.Id, orgId, Arg.Any<CancellationToken>())
            .Returns(user);

        await _handler.HandleAsync(user.Id, orgId, CancellationToken.None);

        Assert.NotEqual(previousStamp, user.SecurityStamp);
    }
}
