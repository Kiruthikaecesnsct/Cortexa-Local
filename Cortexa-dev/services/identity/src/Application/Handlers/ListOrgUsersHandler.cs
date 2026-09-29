using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Interfaces;

namespace Cortexa.Identity.Application.Handlers;

public sealed class ListOrgUsersHandler
{
    private readonly IUserRepository _userRepo;

    public ListOrgUsersHandler(IUserRepository userRepo)
    {
        _userRepo = userRepo;
    }

    public async Task<IReadOnlyList<UserResponse>> HandleAsync(Guid orgId, CancellationToken ct)
    {
        var users = await _userRepo.ListByOrgAsync(orgId, ct);
        return users.Select(UserResponse.From).ToList().AsReadOnly();
    }
}
