using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;

namespace Cortexa.Identity.Application.Handlers;

public sealed class UpdateRolePermissionsHandler
{
    private readonly IPermissionRepository _permRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly IUserRepository _userRepo;
    private readonly IAuditWriter _auditWriter;

    public UpdateRolePermissionsHandler(
        IPermissionRepository permRepo,
        IRefreshTokenRepository refreshTokenRepo,
        IUserRepository userRepo,
        IAuditWriter auditWriter)
    {
        _permRepo = permRepo;
        _refreshTokenRepo = refreshTokenRepo;
        _userRepo = userRepo;
        _auditWriter = auditWriter;
    }

    public async Task HandleAsync(string roleId, UpdateRolePermissionsRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<Role>(roleId, ignoreCase: true, out var role))
            throw new BadRequestException($"Unknown role: {roleId}");

        if (role == Role.SuperAdmin)
            throw new ForbiddenException("SuperAdmin role permissions cannot be modified.");

        if (!await _permRepo.AllExistAsync(request.PermissionNames, ct))
            throw new BadRequestException("One or more permission names are unknown.");

        var previousPermissions = await _permRepo.GetByRoleAsync(role, ct) ?? Array.Empty<Permission>();
        var beforeNames = previousPermissions.Select(p => p.Name).OrderBy(n => n).ToList();

        var allPermissions = await _permRepo.GetAllAsync(ct);
        var selected = allPermissions
            .Where(p => request.PermissionNames.Contains(p.Name))
            .ToList();

        await _permRepo.StageRolePermissionsReplaceAsync(role, selected, ct);

        await _refreshTokenRepo.StageRevokeAllActiveByRoleAsync(role, ct);
        await _userRepo.StageRotateStampByRoleAsync(role, ct);

        await _permRepo.SaveChangesAsync(ct);

        var afterNames = selected.Select(p => p.Name).OrderBy(n => n).ToList();
        await _auditWriter.LogAsync(
            AuditEventType.RolePermissionsChanged,
            "Role",
            role.ToString(),
            "update-role-permissions",
            new { role = role.ToString(), before = beforeNames, after = afterNames },
            ct);
    }
}
