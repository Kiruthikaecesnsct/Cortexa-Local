using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Application.Tests.Helpers;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;
using NSubstitute;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Handlers;

public sealed class CrossOrgAccessTests
{
    private readonly IUserRepository _userRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IAuditWriter _auditWriter;

    public CrossOrgAccessTests()
    {
        _userRepo = Substitute.For<IUserRepository>();
        _refreshTokenRepo = Substitute.For<IRefreshTokenRepository>();
        _auditWriter = Substitute.For<IAuditWriter>();
    }

    [Fact]
    public async Task GetOrgUser_UserInDifferentOrg_ThrowsForbidden()
    {
        var targetUserId = Guid.NewGuid();
        var attackerOrgId = Guid.NewGuid();

        _userRepo.GetByIdInOrgAsync(targetUserId, attackerOrgId, Arg.Any<CancellationToken>())
            .Returns((Domain.Entities.User?)null);

        var handler = new GetOrgUserHandler(_userRepo);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => handler.HandleAsync(targetUserId, attackerOrgId, CancellationToken.None));
    }

    [Fact]
    public async Task GetOrgUser_UserInSameOrg_ReturnsUserResponse()
    {
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(role: Role.Researcher, isEnabled: true, organizationId: orgId);

        _userRepo.GetByIdInOrgAsync(user.Id, orgId, Arg.Any<CancellationToken>())
            .Returns(user);

        var handler = new GetOrgUserHandler(_userRepo);

        var response = await handler.HandleAsync(user.Id, orgId, CancellationToken.None);

        Assert.Equal(user.Id, response.Id);
        Assert.Equal(user.Email, response.Email);
        Assert.Equal(orgId, response.OrganizationId);
    }

    [Fact]
    public async Task DisableOrgUser_UserInDifferentOrg_ThrowsForbidden()
    {
        var targetUserId = Guid.NewGuid();
        var attackerOrgId = Guid.NewGuid();

        _userRepo.GetByIdInOrgAsync(targetUserId, attackerOrgId, Arg.Any<CancellationToken>())
            .Returns((Domain.Entities.User?)null);

        var handler = new DisableOrgUserHandler(_userRepo, _refreshTokenRepo, _auditWriter);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => handler.HandleAsync(targetUserId, attackerOrgId, CancellationToken.None));
    }

    [Fact]
    public async Task EnableOrgUser_UserInDifferentOrg_ThrowsForbidden()
    {
        var targetUserId = Guid.NewGuid();
        var attackerOrgId = Guid.NewGuid();

        _userRepo.GetByIdInOrgAsync(targetUserId, attackerOrgId, Arg.Any<CancellationToken>())
            .Returns((Domain.Entities.User?)null);

        var handler = new EnableOrgUserHandler(_userRepo, _auditWriter);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => handler.HandleAsync(targetUserId, attackerOrgId, CancellationToken.None));
    }

    [Fact]
    public async Task ChangeOrgUserRole_UserInDifferentOrg_ThrowsForbidden()
    {
        var targetUserId = Guid.NewGuid();
        var attackerOrgId = Guid.NewGuid();
        var request = new ChangeRoleRequest(Role.Reviewer);

        _userRepo.GetByIdInOrgAsync(targetUserId, attackerOrgId, Arg.Any<CancellationToken>())
            .Returns((Domain.Entities.User?)null);

        var handler = new ChangeOrgUserRoleHandler(_userRepo, _refreshTokenRepo, _auditWriter);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => handler.HandleAsync(targetUserId, attackerOrgId, request, CancellationToken.None));
    }
}
