using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;

namespace Cortexa.Identity.Application.Handlers;

public sealed class ChangeOrgUserRoleHandler
{
    private readonly IUserRepository _userRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IAuditWriter _auditWriter;

    public ChangeOrgUserRoleHandler(
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
        Guid orgId,
        ChangeRoleRequest request,
        CancellationToken ct)
    {
        var user = await _userRepo.GetByIdInOrgAsync(userId, orgId, ct);
        if (user is null)
            throw new ForbiddenException("Access denied.");

        if (request.Role == Role.SuperAdmin)
            throw new BadRequestException("SuperAdmin role cannot be assigned");

        await EnsureNotLastAdminDowngradeAsync(user, orgId, request.Role, ct);

        var previousRole = user.Role;
        user.SetRole(request.Role);
        await _refreshTokenRepo.StageRevokeAllForUserAsync(user.Id, ct);
        await _userRepo.SaveChangesAsync(ct);

        await _auditWriter.LogAsync(
            AuditEventType.UserRoleChanged,
            "User",
            user.Id.ToString(),
            "change-org-user-role",
            new { before = previousRole.ToString(), after = user.Role.ToString() },
            ct);
    }

    private async Task EnsureNotLastAdminDowngradeAsync(User user, Guid orgId, Role newRole, CancellationToken ct)
    {
        if (user.Role != Role.Admin || newRole == Role.Admin || !user.IsEnabled) return;
        var remaining = await _userRepo.CountActiveAdminsInOrgAsync(orgId, user.Id, ct);
        if (remaining == 0)
            throw new BadRequestException("Cannot downgrade the last Admin in the organization");
    }
}
