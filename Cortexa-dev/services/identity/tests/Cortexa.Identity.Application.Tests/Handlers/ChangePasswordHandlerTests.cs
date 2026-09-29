using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Application.Tests.Helpers;
using Cortexa.Identity.Domain.Exceptions;
using NSubstitute;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Handlers;

public sealed class ChangePasswordHandlerTests
{
    private const int BcryptWorkFactor = 11;

    private readonly IUserRepository _userRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IAuditWriter _auditWriter;
    private readonly ChangePasswordHandler _handler;

    public ChangePasswordHandlerTests()
    {
        _userRepo = Substitute.For<IUserRepository>();
        _refreshTokenRepo = Substitute.For<IRefreshTokenRepository>();
        _auditWriter = Substitute.For<IAuditWriter>();
        _handler = new ChangePasswordHandler(_userRepo, _refreshTokenRepo, _auditWriter);
    }

    [Fact]
    public async Task HandleAsync_CorrectCurrentPassword_UpdatesHashAndRevokesTokens()
    {
        var currentHash = BCrypt.Net.BCrypt.HashPassword("CurrentPass1", BcryptWorkFactor);
        var user = UserBuilder.Build(passwordHash: currentHash);
        var request = new ChangePasswordRequest("CurrentPass1", "NewPassword1");

        _userRepo.GetTrackedByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        await _handler.HandleAsync(user.Id, request, CancellationToken.None);

        Assert.NotEqual(currentHash, user.PasswordHash);
        await _refreshTokenRepo.Received(1).StageRevokeAllForUserAsync(user.Id, Arg.Any<CancellationToken>());
        await _userRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WrongCurrentPassword_ThrowsUnauthorizedException()
    {
        var currentHash = BCrypt.Net.BCrypt.HashPassword("CurrentPass1", BcryptWorkFactor);
        var user = UserBuilder.Build(passwordHash: currentHash);
        var request = new ChangePasswordRequest("WrongPassword", "NewPassword1");

        _userRepo.GetTrackedByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.HandleAsync(user.Id, request, CancellationToken.None));

        await _refreshTokenRepo.DidNotReceive().StageRevokeAllForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_EntraUserWithNullHash_ThrowsBadRequestException()
    {
        var user = UserBuilder.Build(passwordHash: null, entraObjectId: "entra-obj-id");
        var request = new ChangePasswordRequest("AnyPassword1", "NewPassword1");

        _userRepo.GetTrackedByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(user.Id, request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_WeakNewPassword_ThrowsBadRequestException()
    {
        var currentHash = BCrypt.Net.BCrypt.HashPassword("CurrentPass1", BcryptWorkFactor);
        var user = UserBuilder.Build(passwordHash: currentHash);
        var request = new ChangePasswordRequest("CurrentPass1", "short");

        _userRepo.GetTrackedByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        await Assert.ThrowsAsync<BadRequestException>(
            () => _handler.HandleAsync(user.Id, request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_UserNotFound_ThrowsForbiddenException()
    {
        var userId = Guid.NewGuid();
        var request = new ChangePasswordRequest("CurrentPass1", "NewPassword1");

        _userRepo.GetTrackedByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns((Domain.Entities.User?)null);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _handler.HandleAsync(userId, request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_RevocationOnlyTargetsRequestingUser()
    {
        var currentHash = BCrypt.Net.BCrypt.HashPassword("CurrentPass1", BcryptWorkFactor);
        var targetUser = UserBuilder.Build(passwordHash: currentHash);
        var otherUserId = Guid.NewGuid();
        var request = new ChangePasswordRequest("CurrentPass1", "NewPassword1");

        _userRepo.GetTrackedByIdAsync(targetUser.Id, Arg.Any<CancellationToken>()).Returns(targetUser);

        await _handler.HandleAsync(targetUser.Id, request, CancellationToken.None);

        await _refreshTokenRepo.Received(1).StageRevokeAllForUserAsync(targetUser.Id, Arg.Any<CancellationToken>());
        await _refreshTokenRepo.DidNotReceive().StageRevokeAllForUserAsync(otherUserId, Arg.Any<CancellationToken>());
    }
}
