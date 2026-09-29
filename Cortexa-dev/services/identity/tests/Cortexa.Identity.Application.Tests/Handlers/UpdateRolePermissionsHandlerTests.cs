using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;
using NSubstitute;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Handlers;

public sealed class UpdateRolePermissionsHandlerTests
{
    private const string ValidRole = "Researcher";
    private const string UnknownRole = "Overlord";
    private const string PermissionNameA = "patents.read";
    private const string PermissionNameB = "patents.write";

    private readonly IPermissionRepository _permRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IUserRepository _userRepo;
    private readonly IAuditWriter _auditWriter;
    private readonly UpdateRolePermissionsHandler _handler;

    public UpdateRolePermissionsHandlerTests()
    {
        _permRepo = Substitute.For<IPermissionRepository>();
        _refreshTokenRepo = Substitute.For<IRefreshTokenRepository>();
        _userRepo = Substitute.For<IUserRepository>();
        _auditWriter = Substitute.For<IAuditWriter>();
        _handler = new UpdateRolePermissionsHandler(_permRepo, _refreshTokenRepo, _userRepo, _auditWriter);
    }

    [Fact]
    public async Task HandleAsync_UnknownRoleId_ThrowsBadRequestException()
    {
        var request = new UpdateRolePermissionsRequest([]);

        await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(UnknownRole, request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_SuperAdminRole_ThrowsForbiddenException()
    {
        var request = new UpdateRolePermissionsRequest([]);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _handler.HandleAsync("SuperAdmin", request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_UnknownPermissionName_ThrowsBadRequestException()
    {
        var request = new UpdateRolePermissionsRequest([PermissionNameA]);
        _permRepo.AllExistAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(ValidRole, request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_StagesRolePermissionsReplaceWithMatchingPermissions()
    {
        var permA = Permission.Create(PermissionNameA, "read desc");
        var permB = Permission.Create(PermissionNameB, "write desc");
        var request = new UpdateRolePermissionsRequest([PermissionNameA]);

        _permRepo.AllExistAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _permRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Permission> { permA, permB });

        IReadOnlyList<Permission>? capturedPermissions = null;
        await _permRepo.StageRolePermissionsReplaceAsync(
            Arg.Any<Role>(),
            Arg.Do<IReadOnlyList<Permission>>(p => capturedPermissions = p),
            Arg.Any<CancellationToken>());

        await _handler.HandleAsync(ValidRole, request, CancellationToken.None);

        Assert.NotNull(capturedPermissions);
        Assert.Single(capturedPermissions!);
        Assert.Equal(PermissionNameA, capturedPermissions![0].Name);
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_StagesRevokeAllActiveByRole()
    {
        var permA = Permission.Create(PermissionNameA, "read desc");
        var request = new UpdateRolePermissionsRequest([PermissionNameA]);

        _permRepo.AllExistAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _permRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Permission> { permA });

        await _handler.HandleAsync(ValidRole, request, CancellationToken.None);

        await _refreshTokenRepo.Received(1).StageRevokeAllActiveByRoleAsync(Role.Researcher, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_StagesStampRotationForRole()
    {
        var permA = Permission.Create(PermissionNameA, "read desc");
        var request = new UpdateRolePermissionsRequest([PermissionNameA]);

        _permRepo.AllExistAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _permRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Permission> { permA });

        await _handler.HandleAsync(ValidRole, request, CancellationToken.None);

        await _userRepo.Received(1).StageRotateStampByRoleAsync(Role.Researcher, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_SavesChangesExactlyOnce()
    {
        var permA = Permission.Create(PermissionNameA, "read desc");
        var request = new UpdateRolePermissionsRequest([PermissionNameA]);

        _permRepo.AllExistAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _permRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Permission> { permA });

        await _handler.HandleAsync(ValidRole, request, CancellationToken.None);

        await _permRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_EmptyPermissionList_CompletesWithoutException()
    {
        var request = new UpdateRolePermissionsRequest([]);

        _permRepo.AllExistAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _permRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Permission>());

        var exception = await Record.ExceptionAsync(
            () => _handler.HandleAsync(ValidRole, request, CancellationToken.None));

        Assert.Null(exception);
    }
}
