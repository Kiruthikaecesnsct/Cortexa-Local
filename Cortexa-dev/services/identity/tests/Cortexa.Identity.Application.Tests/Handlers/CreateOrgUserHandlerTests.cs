using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Application.Tests.Helpers;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;
using NSubstitute;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Handlers;

public sealed class CreateOrgUserHandlerTests
{
    private const string ValidEmail = "newuser@cortexa.io";
    private const string ValidUsername = "newuser";
    private const string ValidPassword = "P@ssw0rd!";

    private readonly IUserRepository _userRepo;
    private readonly IAuditWriter _auditWriter;
    private readonly CreateOrgUserHandler _handler;

    public CreateOrgUserHandlerTests()
    {
        _userRepo = Substitute.For<IUserRepository>();
        _userRepo.UsernameExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _auditWriter = Substitute.For<IAuditWriter>();
        _handler = new CreateOrgUserHandler(_userRepo, _auditWriter);
    }

    [Fact]
    public async Task HandleAsync_SuperAdminRole_ThrowsBadRequestException()
    {
        var orgId = Guid.NewGuid();
        var request = new CreateUserRequest(ValidEmail, ValidUsername, ValidPassword, Role.SuperAdmin);

        await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(request, orgId, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_EmptyUsername_ThrowsBadRequestException()
    {
        var orgId = Guid.NewGuid();
        var request = new CreateUserRequest(ValidEmail, "", ValidPassword, Role.Researcher);

        await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(request, orgId, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_InvalidEmail_ThrowsBadRequestException()
    {
        var orgId = Guid.NewGuid();
        var request = new CreateUserRequest("not-an-email", ValidUsername, ValidPassword, Role.Researcher);

        await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(request, orgId, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_PasswordTooShort_ThrowsBadRequestException()
    {
        var orgId = Guid.NewGuid();
        var request = new CreateUserRequest(ValidEmail, ValidUsername, "short", Role.Researcher);

        await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(request, orgId, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_EmailAlreadyExistsInOrg_ThrowsBadRequestException()
    {
        var orgId = Guid.NewGuid();
        var request = new CreateUserRequest(ValidEmail, ValidUsername, ValidPassword, Role.Researcher);

        _userRepo.EmailExistsInOrgAsync(ValidEmail, orgId, Arg.Any<CancellationToken>())
            .Returns(true);

        await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(request, orgId, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_UsernameAlreadyExists_ThrowsBadRequestException()
    {
        var orgId = Guid.NewGuid();
        var request = new CreateUserRequest(ValidEmail, ValidUsername, ValidPassword, Role.Researcher);

        _userRepo.EmailExistsInOrgAsync(ValidEmail, orgId, Arg.Any<CancellationToken>())
            .Returns(false);
        _userRepo.UsernameExistsAsync(ValidUsername, Arg.Any<CancellationToken>())
            .Returns(true);

        await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(request, orgId, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_CallsAddAsyncExactlyOnce()
    {
        var orgId = Guid.NewGuid();
        var request = new CreateUserRequest(ValidEmail, ValidUsername, ValidPassword, Role.Researcher);

        _userRepo.EmailExistsInOrgAsync(ValidEmail, orgId, Arg.Any<CancellationToken>())
            .Returns(false);

        await _handler.HandleAsync(request, orgId, CancellationToken.None);

        await _userRepo.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_ReturnsResponseWithMatchingFields()
    {
        var orgId = Guid.NewGuid();
        var request = new CreateUserRequest(ValidEmail, ValidUsername, ValidPassword, Role.Researcher);

        _userRepo.EmailExistsInOrgAsync(ValidEmail, orgId, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _handler.HandleAsync(request, orgId, CancellationToken.None);

        Assert.Equal(ValidEmail, result.Email);
        Assert.Equal(ValidUsername, result.Username);
        Assert.Equal(Role.Researcher, result.Role);
        Assert.Equal(orgId, result.OrganizationId);
        Assert.True(result.IsEnabled);
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_ResponseDoesNotExposePasswordHash()
    {
        var orgId = Guid.NewGuid();
        var request = new CreateUserRequest(ValidEmail, ValidUsername, ValidPassword, Role.Researcher);

        _userRepo.EmailExistsInOrgAsync(ValidEmail, orgId, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _handler.HandleAsync(request, orgId, CancellationToken.None);

        var responseType = result.GetType();
        Assert.Null(responseType.GetProperty("PasswordHash"));
    }
}
