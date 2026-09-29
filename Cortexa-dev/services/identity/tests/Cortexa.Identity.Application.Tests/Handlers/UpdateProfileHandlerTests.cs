using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Application.Tests.Helpers;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;
using NSubstitute;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Handlers;

public sealed class UpdateProfileHandlerTests
{
    private readonly IUserRepository _userRepo;
    private readonly IAuditWriter _auditWriter;
    private readonly UpdateProfileHandler _handler;

    public UpdateProfileHandlerTests()
    {
        _userRepo = Substitute.For<IUserRepository>();
        _auditWriter = Substitute.For<IAuditWriter>();
        _handler = new UpdateProfileHandler(_userRepo, _auditWriter);
    }

    [Fact]
    public async Task HandleAsync_HappyPath_ReturnsSanitizedUserAndSaves()
    {
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(
            email: "old@example.com",
            username: "olduser",
            passwordHash: "hash",
            role: Role.Researcher,
            organizationId: orgId);
        var request = new UpdateProfileRequest("new@example.com", "newuser");

        _userRepo.GetTrackedByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _userRepo.EmailExistsInOrgExcludingUserAsync("new@example.com", orgId, user.Id, Arg.Any<CancellationToken>())
            .Returns(false);
        _userRepo.UsernameExistsExcludingUserAsync("newuser", user.Id, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _handler.HandleAsync(user.Id, orgId, request, CancellationToken.None);

        Assert.Equal("new@example.com", result.Email);
        Assert.Equal("newuser", result.Username);
        await _userRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_EmailCollidesWithAnotherUserInOrg_ThrowsBadRequestException()
    {
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(email: "old@example.com", username: "olduser", organizationId: orgId);
        var request = new UpdateProfileRequest("taken@example.com", "newuser");

        _userRepo.GetTrackedByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _userRepo.EmailExistsInOrgExcludingUserAsync("taken@example.com", orgId, user.Id, Arg.Any<CancellationToken>())
            .Returns(true);

        await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(user.Id, orgId, request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_UsernameCollidesWithAnotherUser_ThrowsBadRequestException()
    {
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(email: "old@example.com", username: "olduser", organizationId: orgId);
        var request = new UpdateProfileRequest("old@example.com", "takenuser");

        _userRepo.GetTrackedByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _userRepo.EmailExistsInOrgExcludingUserAsync("old@example.com", orgId, user.Id, Arg.Any<CancellationToken>())
            .Returns(false);
        _userRepo.UsernameExistsExcludingUserAsync("takenuser", user.Id, Arg.Any<CancellationToken>())
            .Returns(true);

        await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(user.Id, orgId, request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ResubmittingOwnCurrentValues_Succeeds()
    {
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(email: "same@example.com", username: "sameuser", organizationId: orgId);
        var request = new UpdateProfileRequest("same@example.com", "sameuser");

        _userRepo.GetTrackedByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _userRepo.EmailExistsInOrgExcludingUserAsync("same@example.com", orgId, user.Id, Arg.Any<CancellationToken>())
            .Returns(false);
        _userRepo.UsernameExistsExcludingUserAsync("sameuser", user.Id, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _handler.HandleAsync(user.Id, orgId, request, CancellationToken.None);

        Assert.Equal("same@example.com", result.Email);
        Assert.Equal("sameuser", result.Username);
    }

    [Fact]
    public async Task HandleAsync_UserNotFound_ThrowsForbiddenException()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var request = new UpdateProfileRequest("new@example.com", "newuser");

        _userRepo.GetTrackedByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns((Domain.Entities.User?)null);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _handler.HandleAsync(userId, orgId, request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ImmutableUser_ThrowsImmutableUserException()
    {
        var orgId = Guid.NewGuid();
        var user = UserBuilder.Build(
            email: "system@example.com",
            username: "systemuser",
            organizationId: orgId,
            isSystem: true);
        var request = new UpdateProfileRequest("new@example.com", "newuser");

        _userRepo.GetTrackedByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _userRepo.EmailExistsInOrgExcludingUserAsync("new@example.com", orgId, user.Id, Arg.Any<CancellationToken>())
            .Returns(false);
        _userRepo.UsernameExistsExcludingUserAsync("newuser", user.Id, Arg.Any<CancellationToken>())
            .Returns(false);

        await Assert.ThrowsAsync<ImmutableUserException>(
            () => _handler.HandleAsync(user.Id, orgId, request, CancellationToken.None));
    }
}
