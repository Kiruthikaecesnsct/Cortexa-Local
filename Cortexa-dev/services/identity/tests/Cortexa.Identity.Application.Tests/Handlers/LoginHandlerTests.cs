using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Application.Tests.Helpers;
using Cortexa.Identity.Domain.Entities;
using NSubstitute;
using Xunit;

namespace Cortexa.Identity.Application.Tests.Handlers;

public sealed class LoginHandlerTests
{
    private const string ValidEmail = "alpha@cortexa.io";
    private const string ValidPassword = "C0rr3ctP@ssword!";
    private const string WrongPassword = "wr0ngP@ssword!";
    private const string MockAccessToken = "mock.access.token";
    private const string MockRawRefreshToken = "raw-refresh-abc123";
    private const string MockRefreshTokenHash = "hashed-refresh-abc123";

    private readonly IUserRepository _userRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly ITokenService _tokenService;
    private readonly IPermissionRepository _permissionRepo;
    private readonly IAuditWriter _auditWriter;
    private readonly IFailedLoginRepository _failedLoginRepo;
    private readonly LoginHandler _handler;

    public LoginHandlerTests()
    {
        _userRepo = Substitute.For<IUserRepository>();
        _refreshTokenRepo = Substitute.For<IRefreshTokenRepository>();
        _tokenService = Substitute.For<ITokenService>();
        _permissionRepo = Substitute.For<IPermissionRepository>();
        _auditWriter = Substitute.For<IAuditWriter>();
        _failedLoginRepo = Substitute.For<IFailedLoginRepository>();
        _permissionRepo.GetPermissionNamesForUserAsync(Arg.Any<Domain.Enums.Role>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        _failedLoginRepo.RegisterFailureAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new LockState(1, null));
        _handler = new LoginHandler(
            _userRepo, _refreshTokenRepo, _tokenService, _permissionRepo, _auditWriter, _failedLoginRepo);
    }

    [Fact]
    public async Task HandleAsync_ValidCredentials_ReturnsAccessTokenAndRawRefreshToken()
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(ValidPassword, 4);
        var user = UserBuilder.Build(email: ValidEmail, passwordHash: passwordHash);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns(user);
        _tokenService.GenerateAccessToken(user, Arg.Any<IReadOnlyCollection<string>>())
            .Returns((MockAccessToken, expiresAt));
        _tokenService.GenerateRefreshToken()
            .Returns((MockRawRefreshToken, MockRefreshTokenHash));
        _refreshTokenRepo.AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var request = new LoginRequest(ValidEmail, ValidPassword);

        var result = await _handler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(MockAccessToken, result.Response.AccessToken);
        Assert.Equal(MockRawRefreshToken, result.RawRefreshToken);
    }

    [Fact]
    public async Task HandleAsync_ValidCredentials_PersistsRefreshTokenHashNotRawToken()
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(ValidPassword, 4);
        var user = UserBuilder.Build(email: ValidEmail, passwordHash: passwordHash);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        RefreshToken? capturedToken = null;

        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns(user);
        _tokenService.GenerateAccessToken(user, Arg.Any<IReadOnlyCollection<string>>())
            .Returns((MockAccessToken, expiresAt));
        _tokenService.GenerateRefreshToken()
            .Returns((MockRawRefreshToken, MockRefreshTokenHash));
        await _refreshTokenRepo.AddAsync(
            Arg.Do<RefreshToken>(t => capturedToken = t),
            Arg.Any<CancellationToken>());

        var request = new LoginRequest(ValidEmail, ValidPassword);

        await _handler.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(capturedToken);
        Assert.Equal(MockRefreshTokenHash, capturedToken!.TokenHash);
        Assert.NotEqual(MockRawRefreshToken, capturedToken.TokenHash);
    }

    [Fact]
    public async Task HandleAsync_WrongPassword_ThrowsUnauthorizedException()
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(ValidPassword, 4);
        var user = UserBuilder.Build(email: ValidEmail, passwordHash: passwordHash);

        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns(user);

        var request = new LoginRequest(ValidEmail, WrongPassword);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_NonExistentEmail_ThrowsUnauthorizedException()
    {
        _userRepo.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var request = new LoginRequest("ghost@cortexa.io", ValidPassword);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_EntraOnlyUserWithNullPasswordHash_ThrowsUnauthorizedException()
    {
        var user = UserBuilder.Build(email: ValidEmail, passwordHash: null);

        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns(user);

        var request = new LoginRequest(ValidEmail, ValidPassword);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_DisabledUser_ThrowsForbiddenException()
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(ValidPassword, 4);
        var user = UserBuilder.Build(email: ValidEmail, passwordHash: passwordHash, isEnabled: false);

        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns(user);

        var request = new LoginRequest(ValidEmail, ValidPassword);

        await Assert.ThrowsAsync<Domain.Exceptions.ForbiddenException>(
            () => _handler.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_AlreadyLockedAccount_ThrowsAccountLockedExceptionWithRetryAfter()
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(ValidPassword, 4);
        var user = UserBuilder.Build(email: ValidEmail, passwordHash: passwordHash);
        var lockedUntil = DateTimeOffset.UtcNow.AddSeconds(30);

        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns(user);
        _failedLoginRepo.GetAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(FailedLoginAttemptBuilder.Build(userId: user.Id, lockedUntil: lockedUntil));

        var request = new LoginRequest(ValidEmail, ValidPassword);

        var ex = await Assert.ThrowsAsync<Domain.Exceptions.AccountLockedException>(
            () => _handler.HandleAsync(request, CancellationToken.None));

        Assert.True(ex.RetryAfter > TimeSpan.Zero);
        Assert.True(ex.RetryAfter <= TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task HandleAsync_WrongPassword_RegistersFailureAgainstFailedLoginRepository()
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(ValidPassword, 4);
        var user = UserBuilder.Build(email: ValidEmail, passwordHash: passwordHash);

        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns(user);

        var request = new LoginRequest(ValidEmail, WrongPassword);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.HandleAsync(request, CancellationToken.None));

        await _failedLoginRepo.Received(1).RegisterFailureAsync(user.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_FailureCrossesLockoutThreshold_ThrowsAccountLockedException()
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(ValidPassword, 4);
        var user = UserBuilder.Build(email: ValidEmail, passwordHash: passwordHash);
        var lockedUntil = DateTimeOffset.UtcNow.AddSeconds(30);

        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns(user);
        _failedLoginRepo.RegisterFailureAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(new LockState(5, lockedUntil));

        var request = new LoginRequest(ValidEmail, WrongPassword);

        await Assert.ThrowsAsync<Domain.Exceptions.AccountLockedException>(
            () => _handler.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_NullPassword_ThrowsUnauthorizedExceptionNotArgumentNullException()
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(ValidPassword, 4);
        var user = UserBuilder.Build(email: ValidEmail, passwordHash: passwordHash);

        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns(user);

        var request = new LoginRequest(ValidEmail, null!);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.HandleAsync(request, CancellationToken.None));

        await _failedLoginRepo.Received(1).RegisterFailureAsync(user.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_EmptyOrWhitespacePassword_ThrowsUnauthorizedExceptionNotArgumentNullException()
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(ValidPassword, 4);
        var user = UserBuilder.Build(email: ValidEmail, passwordHash: passwordHash);

        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns(user);

        var request = new LoginRequest(ValidEmail, "   ");

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.HandleAsync(request, CancellationToken.None));

        await _failedLoginRepo.Received(1).RegisterFailureAsync(user.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NullEmail_ThrowsUnauthorizedExceptionNotArgumentNullException()
    {
        _userRepo.GetByEmailAsync(string.Empty, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var request = new LoginRequest(null!, ValidPassword);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.HandleAsync(request, CancellationToken.None));

        await _auditWriter.Received(1).LogAuthAsync(
            Domain.Enums.AuditEventType.LoginFailed,
            null,
            "login",
            success: false,
            "user-not-found",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NullEmailAndNullPassword_ThrowsUnauthorizedExceptionNotArgumentNullException()
    {
        _userRepo.GetByEmailAsync(string.Empty, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var request = new LoginRequest(null!, null!);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _handler.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ValidCredentials_ResetsFailedLoginCounter()
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(ValidPassword, 4);
        var user = UserBuilder.Build(email: ValidEmail, passwordHash: passwordHash);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        _userRepo.GetByEmailAsync(ValidEmail, Arg.Any<CancellationToken>())
            .Returns(user);
        _tokenService.GenerateAccessToken(user, Arg.Any<IReadOnlyCollection<string>>())
            .Returns((MockAccessToken, expiresAt));
        _tokenService.GenerateRefreshToken()
            .Returns((MockRawRefreshToken, MockRefreshTokenHash));

        var request = new LoginRequest(ValidEmail, ValidPassword);

        await _handler.HandleAsync(request, CancellationToken.None);

        await _failedLoginRepo.Received(1).ResetAsync(user.Id, Arg.Any<CancellationToken>());
    }
}
