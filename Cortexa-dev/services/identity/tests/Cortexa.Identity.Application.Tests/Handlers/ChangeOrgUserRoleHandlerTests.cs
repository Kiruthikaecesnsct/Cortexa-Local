using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Application.Tests.Helpers;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;
using NSubstitute;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Handlers;

public sealed class ChangeOrgUserRoleHandlerTests
{
    private readonly IUserRepository _userRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IAuditWriter _auditWriter;
    private readonly ChangeOrgUserRoleHandler _handler;

    public ChangeOrgUserRoleHandlerTests()
    {
        _userRepo = Substitute.For<IUserRepository>();
        _refreshTokenRepo = Substitute.For<IRefreshTokenRepository>();
        _auditWriter = Substitute.For<IAuditWriter>();
        _handler = new ChangeOrgUserRoleHandler(_userRepo, _refreshTokenRepo, _auditWriter);
    }

    [Fact]
    public async Task HandleAsync_UserNotInOrg_ThrowsForbiddenException()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var request = new ChangeRoleRequest(Role.Researcher);

        _userRepo.GetByIdInOrgAsync(userId, orgId, Arg.Any<CancellationToken>())
            .Returns((Domain.Entities.User?)null);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _handler.HandleAsync(userId, orgId, request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_SuperAdminRoleRequested_ThrowsBadRequestException()
    {
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(role: Role.Researcher, isEnabled: true, organizationId: orgId);
        var request = new ChangeRoleRequest(Role.SuperAdmin);

        _userRepo.GetByIdInOrgAsync(user.Id, orgId, Arg.Any<CancellationToken>())
            .Returns(user);

        await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(user.Id, orgId, request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_LastActiveAdminDowngraded_ThrowsBadRequestException()
    {
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(role: Role.Admin, isEnabled: true, organizationId: orgId);
        var request = new ChangeRoleRequest(Role.Researcher);

        _userRepo.GetByIdInOrgAsync(user.Id, orgId, Arg.Any<CancellationToken>())
            .Returns(user);
        _userRepo.CountActiveAdminsInOrgAsync(orgId, user.Id, Arg.Any<CancellationToken>())
            .Returns(0);

        await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(user.Id, orgId, request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ValidRoleChange_CallsSaveChangesOnce()
    {
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(role: Role.Researcher, isEnabled: true, organizationId: orgId);
        var request = new ChangeRoleRequest(Role.Admin);

        _userRepo.GetByIdInOrgAsync(user.Id, orgId, Arg.Any<CancellationToken>())
            .Returns(user);

        await _handler.HandleAsync(user.Id, orgId, request, CancellationToken.None);

        await _userRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidRoleChange_RevokesAllRefreshTokensForUser()
    {
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(role: Role.Researcher, isEnabled: true, organizationId: orgId);
        var request = new ChangeRoleRequest(Role.Admin);

        _userRepo.GetByIdInOrgAsync(user.Id, orgId, Arg.Any<CancellationToken>())
            .Returns(user);

        await _handler.HandleAsync(user.Id, orgId, request, CancellationToken.None);

        await _refreshTokenRepo.Received(1).StageRevokeAllForUserAsync(user.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidRoleChange_RotatesUserSecurityStamp()
    {
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(role: Role.Researcher, isEnabled: true, organizationId: orgId);
        var previousStamp = user.SecurityStamp;
        var request = new ChangeRoleRequest(Role.Admin);

        _userRepo.GetByIdInOrgAsync(user.Id, orgId, Arg.Any<CancellationToken>())
            .Returns(user);

        await _handler.HandleAsync(user.Id, orgId, request, CancellationToken.None);

        Assert.NotEqual(previousStamp, user.SecurityStamp);
    }
}
