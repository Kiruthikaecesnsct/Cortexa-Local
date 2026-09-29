using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;

namespace Cortexa.Identity.Application.Handlers;

public sealed class DisableOrgUserHandler
{
    private readonly IUserRepository _userRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IAuditWriter _auditWriter;

    public DisableOrgUserHandler(
        IUserRepository userRepo,
        IRefreshTokenRepository refreshTokenRepo,
        IAuditWriter auditWriter)
    {
        _userRepo = userRepo;
        _refreshTokenRepo = refreshTokenRepo;
        _auditWriter = auditWriter;
    }

    public async Task HandleAsync(Guid userId, Guid orgId, CancellationToken ct)
    {
        var user = await _userRepo.GetByIdInOrgAsync(userId, orgId, ct);
        if (user is null)
            throw new ForbiddenException("Access denied.");

        if (user.Role == Role.Admin && user.IsEnabled)
        {
            var remaining = await _userRepo.CountActiveAdminsInOrgAsync(orgId, user.Id, ct);
            if (remaining == 0)
                throw new BadRequestException("Cannot disable the last Admin in the organization");
        }

        user.Disable();
        await _refreshTokenRepo.StageRevokeAllForUserAsync(user.Id, ct);
        await _userRepo.SaveChangesAsync(ct);

        await _auditWriter.LogAsync(
            AuditEventType.UserDisabled,
            "User",
            user.Id.ToString(),
            "disable-org-user",
            new { organizationId = orgId },
            ct);
    }
}
