using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;

namespace Cortexa.Identity.Application.Handlers;

public sealed class EnableOrgUserHandler
{
    private readonly IUserRepository _userRepo;
    private readonly IAuditWriter _auditWriter;

    public EnableOrgUserHandler(IUserRepository userRepo, IAuditWriter auditWriter)
    {
        _userRepo = userRepo;
        _auditWriter = auditWriter;
    }

    public async Task HandleAsync(Guid userId, Guid orgId, CancellationToken ct)
    {
        var user = await _userRepo.GetByIdInOrgAsync(userId, orgId, ct);
        if (user is null)
            throw new ForbiddenException("Access denied.");

        user.Enable();
        await _userRepo.SaveChangesAsync(ct);

        await _auditWriter.LogAsync(
            AuditEventType.UserEnabled,
            "User",
            user.Id.ToString(),
            "enable-org-user",
            new { organizationId = orgId },
            ct);
    }
}
