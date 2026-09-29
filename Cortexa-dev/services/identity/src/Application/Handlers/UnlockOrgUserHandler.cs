using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;

namespace Cortexa.Identity.Application.Handlers;

public sealed class UnlockOrgUserHandler
{
    private readonly IUserRepository _userRepo;
    private readonly IFailedLoginRepository _failedLoginRepo;
    private readonly IAuditWriter _auditWriter;

    public UnlockOrgUserHandler(
        IUserRepository userRepo,
        IFailedLoginRepository failedLoginRepo,
        IAuditWriter auditWriter)
    {
        _userRepo = userRepo;
        _failedLoginRepo = failedLoginRepo;
        _auditWriter = auditWriter;
    }

    public async Task HandleAsync(Guid userId, Guid orgId, CancellationToken ct)
    {
        var user = await _userRepo.GetByIdInOrgAsync(userId, orgId, ct);
        if (user is null)
            throw new ForbiddenException("Access denied.");

        await _failedLoginRepo.ResetAsync(user.Id, ct);

        await _auditWriter.LogAsync(
            AuditEventType.UserUnlocked,
            "User",
            user.Id.ToString(),
            "unlock-org-user",
            new { organizationId = orgId },
            ct);
    }
}
