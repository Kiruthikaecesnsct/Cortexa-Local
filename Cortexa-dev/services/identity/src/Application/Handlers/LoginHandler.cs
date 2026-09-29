using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;

namespace Cortexa.Identity.Application.Handlers;

public sealed class UnauthorizedException : Exception
{
    public UnauthorizedException() : base("Invalid credentials.") { }
}

public sealed class LoginHandler
{
    private const int BcryptWorkFactor = 11;

    private static readonly string DummyHash =
        BCrypt.Net.BCrypt.HashPassword("dummy-password-for-timing-safety", BcryptWorkFactor);

    private readonly IUserRepository _userRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly ITokenService _tokenService;
    private readonly IPermissionRepository _permissionRepo;
    private readonly IAuditWriter _auditWriter;
    private readonly IFailedLoginRepository _failedLoginRepo;

    public LoginHandler(
        IUserRepository userRepo,
        IRefreshTokenRepository refreshTokenRepo,
        ITokenService tokenService,
        IPermissionRepository permissionRepo,
        IAuditWriter auditWriter,
        IFailedLoginRepository failedLoginRepo)
    {
        _userRepo = userRepo;
        _refreshTokenRepo = refreshTokenRepo;
        _tokenService = tokenService;
        _permissionRepo = permissionRepo;
        _auditWriter = auditWriter;
        _failedLoginRepo = failedLoginRepo;
    }

    public async Task<(LoginResponse Response, string RawRefreshToken)> HandleAsync(
        LoginRequest request, CancellationToken ct)
    {
        var email = request.Email ?? string.Empty;
        var password = request.Password ?? string.Empty;

        var user = await _userRepo.GetByEmailAsync(email, ct);
        var hashToVerify = user?.PasswordHash ?? DummyHash;
        var passwordValid = BCrypt.Net.BCrypt.Verify(password, hashToVerify);

        if (user is null || !passwordValid)
        {
            await HandleFailedAttemptAsync(user, ct);
            throw new UnauthorizedException();
        }

        // Only reveal lockout/disabled status once the password is confirmed correct.
        // Otherwise an attacker with a wrong password could learn account state
        // (exists / locked / disabled) from the response before ever proving ownership.
        await EnsureNotLockedAsync(user.Id, ct);
        EnsureEnabled(user);

        await _failedLoginRepo.ResetAsync(user.Id, ct);

        var permissions = await _permissionRepo.GetPermissionNamesForUserAsync(user.Role, ct);
        var (accessToken, expiresAt) = _tokenService.GenerateAccessToken(user, permissions);
        var (rawToken, tokenHash) = _tokenService.GenerateRefreshToken();

        var refreshToken = RefreshToken.Create(user.Id, tokenHash, DateTimeOffset.UtcNow.AddDays(7));
        await _refreshTokenRepo.AddAsync(refreshToken, ct);

        await _auditWriter.LogAuthAsync(
            AuditEventType.LoginSucceeded, user.Id, "login", success: true, failureReason: null, ct);

        return (new LoginResponse(accessToken, expiresAt), rawToken);
    }

    private async Task EnsureNotLockedAsync(Guid userId, CancellationToken ct)
    {
        var lockState = await _failedLoginRepo.GetAsync(userId, ct);
        var now = DateTimeOffset.UtcNow;
        if (lockState is not null && lockState.IsLocked(now))
            throw new AccountLockedException(lockState.RetryAfter(now));
    }

    private static void EnsureEnabled(User user)
    {
        if (!user.IsEnabled)
            throw new ForbiddenException("Account is disabled.");
    }

    private async Task HandleFailedAttemptAsync(User? user, CancellationToken ct)
    {
        var failureReason = user is null ? "user-not-found" : "wrong-password";
        await _auditWriter.LogAuthAsync(
            AuditEventType.LoginFailed, user?.Id, "login", success: false, failureReason, ct);

        if (user is null)
            return;

        var lockState = await _failedLoginRepo.RegisterFailureAsync(user.Id, ct);
        var now = DateTimeOffset.UtcNow;
        if (lockState.LockedUntil is { } lockedUntil && lockedUntil > now)
            throw new AccountLockedException(lockedUntil - now);
    }
}
