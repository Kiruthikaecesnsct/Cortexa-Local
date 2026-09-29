using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Application.Validation;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;

namespace Cortexa.Identity.Application.Handlers;

public sealed class ChangePasswordHandler
{
    private const int BcryptWorkFactor = 11;

    private readonly IUserRepository _userRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IAuditWriter _auditWriter;

    public ChangePasswordHandler(
        IUserRepository userRepo,
        IRefreshTokenRepository refreshTokenRepo,
        IAuditWriter auditWriter)
    {
        _userRepo = userRepo;
        _refreshTokenRepo = refreshTokenRepo;
        _auditWriter = auditWriter;
    }

    public async Task HandleAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken ct)
    {
        var user = await _userRepo.GetTrackedByIdAsync(userId, ct);
        if (user is null)
            throw new ForbiddenException("Access denied.");

        EnsureCurrentPasswordValid(user.PasswordHash, request.CurrentPassword);
        UserFieldValidator.EnsurePasswordStrength(request.NewPassword);

        var newHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, BcryptWorkFactor);
        user.ChangePassword(newHash);

        await _refreshTokenRepo.StageRevokeAllForUserAsync(user.Id, ct);
        await _userRepo.SaveChangesAsync(ct);

        await _auditWriter.LogAsync(
            AuditEventType.UserPasswordChanged,
            "User",
            user.Id.ToString(),
            "change-password",
            details: null,
            ct);
    }

    private static void EnsureCurrentPasswordValid(string? passwordHash, string currentPassword)
    {
        if (passwordHash is null)
            throw new BadRequestException("This account has no local password to change");

        if (!BCrypt.Net.BCrypt.Verify(currentPassword, passwordHash))
            throw new UnauthorizedException();
    }
}
