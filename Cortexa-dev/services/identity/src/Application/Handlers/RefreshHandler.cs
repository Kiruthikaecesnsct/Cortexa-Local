using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;
using Microsoft.IdentityModel.Tokens;

namespace Cortexa.Identity.Application.Handlers;

public sealed class RefreshHandler
{
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IUserRepository _userRepo;
    private readonly ITokenService _tokenService;
    private readonly IPermissionRepository _permissionRepo;
    private readonly IAuditWriter _auditWriter;

    public RefreshHandler(
        IRefreshTokenRepository refreshTokenRepo,
        IUserRepository userRepo,
        ITokenService tokenService,
        IPermissionRepository permissionRepo,
        IAuditWriter auditWriter)
    {
        _refreshTokenRepo = refreshTokenRepo;
        _userRepo = userRepo;
        _tokenService = tokenService;
        _permissionRepo = permissionRepo;
        _auditWriter = auditWriter;
    }

    public async Task<(LoginResponse Response, string RawRefreshToken)> HandleAsync(
        string rawRefreshToken, CancellationToken ct)
    {
        string tokenHash;
        try
        {
            tokenHash = _tokenService.ComputeTokenHash(rawRefreshToken);
        }
        catch (FormatException)
        {
            await _auditWriter.LogAuthAsync(
                AuditEventType.TokenRefreshFailed, null, "refresh", success: false, "malformed-token", ct);
            throw new UnauthorizedException();
        }

        var token = await _refreshTokenRepo.GetByTokenHashAsync(tokenHash, ct);
        if (token is null || !token.IsActive)
        {
            var reason = token is null ? "token-not-found" : DescribeInactiveReason(token);
            await _auditWriter.LogAuthAsync(
                AuditEventType.TokenRefreshFailed, token?.UserId, "refresh", success: false, reason, ct);

            if (token is not null && token.Revoked)
            {
                await _auditWriter.LogAuthAsync(
                    AuditEventType.TokenRevoked, token.UserId, "refresh-reuse-detected", success: false, reason, ct);
            }

            throw new UnauthorizedException();
        }

        var user = await _userRepo.GetByIdAsync(token.UserId, ct);
        if (user is null)
        {
            await _auditWriter.LogAuthAsync(
                AuditEventType.TokenRefreshFailed, token.UserId, "refresh", success: false, "user-not-found", ct);
            throw new UnauthorizedException();
        }

        if (!user.IsEnabled)
        {
            await _auditWriter.LogAuthAsync(
                AuditEventType.TokenRefreshFailed, user.Id, "refresh", success: false, "user-disabled", ct);
            throw new ForbiddenException("Account is disabled.");
        }

        token.MarkUsed();

        var permissions = await _permissionRepo.GetPermissionNamesForUserAsync(user.Role, ct);
        var (accessToken, expiresAt) = _tokenService.GenerateAccessToken(user, permissions);
        var (newRawToken, newTokenHash) = _tokenService.GenerateRefreshToken();

        var newToken = RefreshToken.Create(user.Id, newTokenHash, DateTimeOffset.UtcNow.AddDays(7));
        await _refreshTokenRepo.AddAsync(newToken, ct);

        await _auditWriter.LogAuthAsync(
            AuditEventType.TokenRefreshed, user.Id, "refresh", success: true, failureReason: null, ct);

        return (new LoginResponse(accessToken, expiresAt), newRawToken);
    }

    private static string DescribeInactiveReason(RefreshToken token)
    {
        if (token.Revoked)
            return "token-revoked";
        if (token.UsedAt is not null)
            return "token-already-used";
        return "token-expired";
    }
}
