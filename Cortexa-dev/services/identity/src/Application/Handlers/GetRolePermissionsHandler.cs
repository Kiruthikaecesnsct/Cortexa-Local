using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;

namespace Cortexa.Identity.Application.Handlers;

public sealed class GetRolePermissionsHandler
{
    private readonly IPermissionRepository _repo;

    public GetRolePermissionsHandler(IPermissionRepository repo)
    {
        _repo = repo;
    }

    public async Task<RolePermissionsResponse> HandleAsync(string roleId, CancellationToken ct)
    {
        if (!Enum.TryParse<Role>(roleId, ignoreCase: true, out var role))
            throw new BadRequestException($"Unknown role: {roleId}");

        var permissions = await _repo.GetByRoleAsync(role, ct);
        var dtos = permissions
            .Select(p => new PermissionDto(p.Id, p.Name, p.Description))
            .ToList();

        return new RolePermissionsResponse(role.ToString(), dtos);
    }
}
