using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Application.Tests.Helpers;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Handlers;

public sealed class RefreshHandlerTests
{
    private const string ValidRawToken = "mockRawToken123";
    private const string ValidTokenHash = "mock-hash-abc123";
    private const string MalformedRawToken = "!!!invalid!!!";
    private const string MockAccessToken = "mock.access.token";
    private const string NewMockRawToken = "newRawToken456";
    private const string NewMockTokenHash = "new-hash-def456";

    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IUserRepository _userRepo;
    private readonly ITokenService _tokenService;
    private readonly IPermissionRepository _permissionRepo;
    private readonly IAuditWriter _auditWriter;
    private readonly RefreshHandler _handler;

    public RefreshHandlerTests()
    {
        _refreshTokenRepo = Substitute.For<IRefreshTokenRepository>();
        _userRepo = Substitute.For<IUserRepository>();
        _tokenService = Substitute.For<ITokenService>();
        _permissionRepo = Substitute.For<IPermissionRepository>();
        _auditWriter = Substitute.For<IAuditWriter>();
        _permissionRepo.GetPermissionNamesForUserAsync(Arg.Any<Domain.Enums.Role>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        _handler = new RefreshHandler(_refreshTokenRepo, _userRepo, _tokenService, _permissionRepo, _auditWriter);
    }

    [Fact]
    public async Task HandleAsync_ValidActiveToken_ReturnsNewAccessAndRefreshTokens()
    {
        var userId = Guid.NewGuid();
        var user = UserBuilder.Build(id: userId, role: Role.Reviewer);
        var token = RefreshTokenBuilder.Build(userId: userId, tokenHash: ValidTokenHash);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        _tokenService.ComputeTokenHash(ValidRawToken).Returns(ValidTokenHash);
        _refreshTokenRepo.GetByTokenHashAsync(ValidTokenHash, Arg.Any<CancellationToken>()).Returns(token);
        _userRepo.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
        _tokenService.GenerateAccessToken(user, Arg.Any<IReadOnlyCollection<string>>()).Returns((MockAccessToken, expiresAt));
        _tokenService.GenerateRefreshToken().Returns((NewMockRawToken, NewMockTokenHash));

        var result = await _handler.HandleAsync(ValidRawToken, CancellationToken.None);

        Assert.Equal(MockAccessToken, result.Response.AccessToken);
        Assert.Equal(NewMockRawToken, result.RawRefreshToken);
    }

    [Fact]
    public async Task HandleAsync_ValidToken_MarksOldTokenUsed()
    {
        var userId = Guid.NewGuid();
        var user = UserBuilder.Build(id: userId);
        var token = RefreshTokenBuilder.Build(userId: userId, tokenHash: ValidTokenHash);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        _tokenService.ComputeTokenHash(ValidRawToken).Returns(ValidTokenHash);
        _refreshTokenRepo.GetByTokenHashAsync(ValidTokenHash, Arg.Any<CancellationToken>()).Returns(token);
        _userRepo.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
        _tokenService.GenerateAccessToken(user, Arg.Any<IReadOnlyCollection<string>>()).Returns((MockAccessToken, expiresAt));
        _tokenService.GenerateRefreshToken().Returns((NewMockRawToken, NewMockTokenHash));

        await _handler.HandleAsync(ValidRawToken, CancellationToken.None);

        Assert.NotNull(token.UsedAt);
        Assert.True(token.UsedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task HandleAsync_ValidToken_PersistsNewTokenWithSevenDayExpiry()
    {
        var userId = Guid.NewGuid();
        var user = UserBuilder.Build(id: userId);
        var token = RefreshTokenBuilder.Build(userId: userId, tokenHash: ValidTokenHash);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        RefreshToken? capturedToken = null;

        _tokenService.ComputeTokenHash(ValidRawToken).Returns(ValidTokenHash);
        _refreshTokenRepo.GetByTokenHashAsync(ValidTokenHash, Arg.Any<CancellationToken>()).Returns(token);
        _userRepo.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
        _tokenService.GenerateAccessToken(user, Arg.Any<IReadOnlyCollection<string>>()).Returns((MockAccessToken, expiresAt));
        _tokenService.GenerateRefreshToken().Returns((NewMockRawToken, NewMockTokenHash));
        await _refreshTokenRepo.AddAsync(
            Arg.Do<RefreshToken>(t => capturedToken = t),
            Arg.Any<CancellationToken>());

        await _handler.HandleAsync(ValidRawToken, CancellationToken.None);

        Assert.NotNull(capturedToken);
        Assert.Equal(NewMockTokenHash, capturedToken!.TokenHash);
        Assert.Equal(userId, capturedToken.UserId);
        var sevenDaysFromNow = DateTimeOffset.UtcNow.AddDays(7);
        Assert.True(Math.Abs((capturedToken.ExpiresAt - sevenDaysFromNow).TotalSeconds) < 5);
    }

    [Fact]
    public async Task HandleAsync_UsedToken_ThrowsUnauthorizedException()
    {
        var token = RefreshTokenBuilder.Build(tokenHash: ValidTokenHash, usedAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        _tokenService.ComputeTokenHash(ValidRawToken).Returns(ValidTokenHash);
        _refreshTokenRepo.GetByTokenHashAsync(ValidTokenHash, Arg.Any<CancellationToken>()).Returns(token);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.HandleAsync(ValidRawToken, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_RevokedToken_ThrowsUnauthorizedException()
    {
        var token = RefreshTokenBuilder.Build(tokenHash: ValidTokenHash, revoked: true);

        _tokenService.ComputeTokenHash(ValidRawToken).Returns(ValidTokenHash);
        _refreshTokenRepo.GetByTokenHashAsync(ValidTokenHash, Arg.Any<CancellationToken>()).Returns(token);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.HandleAsync(ValidRawToken, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ExpiredToken_ThrowsUnauthorizedException()
    {
        var token = RefreshTokenBuilder.Build(tokenHash: ValidTokenHash, expiresAt: DateTimeOffset.UtcNow.AddDays(-1));

        _tokenService.ComputeTokenHash(ValidRawToken).Returns(ValidTokenHash);
        _refreshTokenRepo.GetByTokenHashAsync(ValidTokenHash, Arg.Any<CancellationToken>()).Returns(token);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.HandleAsync(ValidRawToken, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_UnknownTokenHash_ThrowsUnauthorizedException()
    {
        _tokenService.ComputeTokenHash(ValidRawToken).Returns(ValidTokenHash);
        _refreshTokenRepo.GetByTokenHashAsync(ValidTokenHash, Arg.Any<CancellationToken>())
            .Returns((RefreshToken?)null);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.HandleAsync(ValidRawToken, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_MalformedRawToken_ThrowsUnauthorizedException()
    {
        _tokenService.ComputeTokenHash(MalformedRawToken)
            .Returns(x => throw new FormatException());

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.HandleAsync(MalformedRawToken, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_UserNotFound_ThrowsUnauthorizedException()
    {
        var userId = Guid.NewGuid();
        var token = RefreshTokenBuilder.Build(userId: userId, tokenHash: ValidTokenHash);

        _tokenService.ComputeTokenHash(ValidRawToken).Returns(ValidTokenHash);
        _refreshTokenRepo.GetByTokenHashAsync(ValidTokenHash, Arg.Any<CancellationToken>()).Returns(token);
        _userRepo.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns((User?)null);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.HandleAsync(ValidRawToken, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_DisabledUser_ThrowsForbiddenExceptionAndDoesNotMarkTokenUsed()
    {
        var userId = Guid.NewGuid();
        var user = UserBuilder.Build(id: userId, isEnabled: false);
        var token = RefreshTokenBuilder.Build(userId: userId, tokenHash: ValidTokenHash);

        _tokenService.ComputeTokenHash(ValidRawToken).Returns(ValidTokenHash);
        _refreshTokenRepo.GetByTokenHashAsync(ValidTokenHash, Arg.Any<CancellationToken>()).Returns(token);
        _userRepo.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _handler.HandleAsync(ValidRawToken, CancellationToken.None));

        Assert.Null(token.UsedAt);
    }

    [Fact]
    public async Task HandleAsync_DisabledUser_LogsTokenRefreshFailed()
    {
        var userId = Guid.NewGuid();
        var user = UserBuilder.Build(id: userId, isEnabled: false);
        var token = RefreshTokenBuilder.Build(userId: userId, tokenHash: ValidTokenHash);

        _tokenService.ComputeTokenHash(ValidRawToken).Returns(ValidTokenHash);
        _refreshTokenRepo.GetByTokenHashAsync(ValidTokenHash, Arg.Any<CancellationToken>()).Returns(token);
        _userRepo.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _handler.HandleAsync(ValidRawToken, CancellationToken.None));

        await _auditWriter.Received(1).LogAuthAsync(
            AuditEventType.TokenRefreshFailed, userId, "refresh", false, "user-disabled", Arg.Any<CancellationToken>());
    }
}
