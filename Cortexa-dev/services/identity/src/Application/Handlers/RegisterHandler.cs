using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Application.Validation;
using Cortexa.Identity.Domain;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;

namespace Cortexa.Identity.Application.Handlers;

public sealed class DuplicateEmailException : Exception
{
    public DuplicateEmailException() : base("Email already registered.") { }
}

public sealed class RegisterHandler
{
    private const int BcryptWorkFactor = 11;

    private readonly IUserRepository _userRepo;
    private readonly IAuditWriter _auditWriter;

    public RegisterHandler(IUserRepository userRepo, IAuditWriter auditWriter)
    {
        _userRepo = userRepo;
        _auditWriter = auditWriter;
    }

    public async Task<RegisterResponse> HandleAsync(
        RegisterRequest request,
        CancellationToken ct)
    {
        UserFieldValidator.EnsureValidEmail(request.Email);
        UserFieldValidator.EnsurePasswordStrength(request.Password);
        UserFieldValidator.EnsureValidUsername(request.DisplayName);

        var existing = await _userRepo.GetByEmailAsync(request.Email, ct);

        if (existing is not null)
            throw new DuplicateEmailException();

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(
            request.Password,
            BcryptWorkFactor);

        var user = User.CreateWithPassword(
            request.Email,
            request.DisplayName,
            passwordHash,
            Role.Researcher);

        user.AssignOrganization(OrganizationConstants.DefaultOrganizationId);

        await _userRepo.AddAsync(user, ct);

        await _auditWriter.LogAsync(
            AuditEventType.UserCreated,
            "User",
            user.Id.ToString(),
            "self-register",
            new { role = user.Role.ToString(), source = "self-register" },
            ct);

        return new RegisterResponse(
            user.Id,
            user.Email,
            user.Username);
    }
}
