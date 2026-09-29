using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Exceptions;

namespace Cortexa.Identity.Application.Handlers;

public sealed class GetOrgUserHandler
{
    private readonly IUserRepository _userRepo;

    public GetOrgUserHandler(IUserRepository userRepo)
    {
        _userRepo = userRepo;
    }

    public async Task<UserResponse> HandleAsync(Guid userId, Guid orgId, CancellationToken ct)
    {
        var user = await _userRepo.GetByIdInOrgAsync(userId, orgId, ct);
        if (user is null)
            throw new ForbiddenException("Access denied.");
        return UserResponse.From(user);
    }
}
