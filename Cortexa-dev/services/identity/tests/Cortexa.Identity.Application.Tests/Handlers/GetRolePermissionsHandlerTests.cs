using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;
using NSubstitute;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Handlers;

public sealed class GetRolePermissionsHandlerTests
{
    private const string UnknownRole = "Overlord";
    private const string ValidRole = "Researcher";
    private const string PermissionName = "patents.read";
    private const string PermissionDescription = "Allows reading patents";

    private readonly IPermissionRepository _repo;
    private readonly GetRolePermissionsHandler _handler;

    public GetRolePermissionsHandlerTests()
    {
        _repo = Substitute.For<IPermissionRepository>();
        _handler = new GetRolePermissionsHandler(_repo);
    }

    [Fact]
    public async Task HandleAsync_UnknownRoleId_ThrowsBadRequestException()
    {
        await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(UnknownRole, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ValidRole_ReturnsResponseWithMatchingRoleName()
    {
        _repo.GetByRoleAsync(Role.Researcher, Arg.Any<CancellationToken>())
            .Returns(new List<Permission>());

        var result = await _handler.HandleAsync(ValidRole, CancellationToken.None);

        Assert.Equal("Researcher", result.Role);
    }

    [Fact]
    public async Task HandleAsync_ValidRole_ReturnsMappedPermissionsWithCorrectFields()
    {
        var perm = Permission.Create(PermissionName, PermissionDescription);

        _repo.GetByRoleAsync(Role.Researcher, Arg.Any<CancellationToken>())
            .Returns(new List<Permission> { perm });

        var result = await _handler.HandleAsync(ValidRole, CancellationToken.None);

        Assert.Single(result.Permissions);
        Assert.Equal(perm.Id, result.Permissions[0].Id);
        Assert.Equal(PermissionName, result.Permissions[0].Name);
        Assert.Equal(PermissionDescription, result.Permissions[0].Description);
    }

    [Fact]
    public async Task HandleAsync_ValidRoleWithNoPermissions_ReturnsEmptyPermissionList()
    {
        _repo.GetByRoleAsync(Role.Researcher, Arg.Any<CancellationToken>())
            .Returns(new List<Permission>());

        var result = await _handler.HandleAsync(ValidRole, CancellationToken.None);

        Assert.Empty(result.Permissions);
    }
}
