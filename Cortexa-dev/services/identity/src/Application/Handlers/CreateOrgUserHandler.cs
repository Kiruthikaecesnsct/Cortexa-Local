using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Application.Validation;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;

namespace Cortexa.Identity.Application.Handlers;

public sealed class CreateOrgUserHandler
{
    private const int BcryptWorkFactor = 11;

    private readonly IUserRepository _userRepo;
    private readonly IAuditWriter _auditWriter;

    public CreateOrgUserHandler(IUserRepository userRepo, IAuditWriter auditWriter)
    {
        _userRepo = userRepo;
        _auditWriter = auditWriter;
    }

    public async Task<UserResponse> HandleAsync(
        CreateUserRequest request,
        Guid orgId,
        CancellationToken ct)
    {
        ValidateRequest(request);

        if (await _userRepo.EmailExistsInOrgAsync(request.Email, orgId, ct))
            throw new BadRequestException("Email already exists in this organization");

        if (await _userRepo.UsernameExistsAsync(request.Username, ct))
            throw new BadRequestException("Username already taken");

        var hash = BCrypt.Net.BCrypt.HashPassword(request.Password, BcryptWorkFactor);
        var user = User.CreateWithPassword(request.Email, request.Username, hash, request.Role);
        user.AssignOrganization(orgId);

        await _userRepo.AddAsync(user, ct);

        await _auditWriter.LogAsync(
            AuditEventType.UserCreated,
            "User",
            user.Id.ToString(),
            "create-org-user",
            new { role = user.Role.ToString(), organizationId = orgId },
            ct);

        return UserResponse.From(user);
    }

    private static void ValidateRequest(CreateUserRequest request)
    {
        UserFieldValidator.EnsureValidUsername(request.Username);
        UserFieldValidator.EnsureValidEmail(request.Email);
        UserFieldValidator.EnsurePasswordStrength(request.Password);

        if (request.Role == Role.SuperAdmin)
            throw new BadRequestException("SuperAdmin role cannot be assigned");
    }
}
