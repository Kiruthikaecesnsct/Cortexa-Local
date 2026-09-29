using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Interfaces;

namespace Cortexa.Identity.Application.Handlers;

public sealed class GetPermissionsHandler
{
    private readonly IPermissionRepository _repo;

    public GetPermissionsHandler(IPermissionRepository repo)
    {
        _repo = repo;
    }

    public async Task<IReadOnlyList<PermissionDto>> HandleAsync(CancellationToken ct)
    {
        var permissions = await _repo.GetAllAsync(ct);
        return permissions
            .Select(p => new PermissionDto(p.Id, p.Name, p.Description))
            .ToList();
    }
}
