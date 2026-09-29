using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Entities;
using NSubstitute;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Handlers;

public sealed class GetPermissionsHandlerTests
{
    private const string PermissionNameA = "patents.read";
    private const string PermissionDescriptionA = "Allows reading patents";
    private const string PermissionNameB = "patents.write";
    private const string PermissionDescriptionB = "Allows writing patents";

    private readonly IPermissionRepository _repo;
    private readonly GetPermissionsHandler _handler;

    public GetPermissionsHandlerTests()
    {
        _repo = Substitute.For<IPermissionRepository>();
        _handler = new GetPermissionsHandler(_repo);
    }

    [Fact]
    public async Task HandleAsync_WithMultiplePermissions_ReturnsAllMappedDtos()
    {
        var permA = Permission.Create(PermissionNameA, PermissionDescriptionA);
        var permB = Permission.Create(PermissionNameB, PermissionDescriptionB);

        _repo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Permission> { permA, permB });

        var result = await _handler.HandleAsync(CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task HandleAsync_WithPermission_MapsIdNameDescriptionCorrectly()
    {
        var perm = Permission.Create(PermissionNameA, PermissionDescriptionA);

        _repo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Permission> { perm });

        var result = await _handler.HandleAsync(CancellationToken.None);

        var dto = result[0];
        Assert.Equal(perm.Id, dto.Id);
        Assert.Equal(PermissionNameA, dto.Name);
        Assert.Equal(PermissionDescriptionA, dto.Description);
    }

    [Fact]
    public async Task HandleAsync_NoPermissions_ReturnsEmptyList()
    {
        _repo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Permission>());

        var result = await _handler.HandleAsync(CancellationToken.None);

        Assert.Empty(result);
    }
}
