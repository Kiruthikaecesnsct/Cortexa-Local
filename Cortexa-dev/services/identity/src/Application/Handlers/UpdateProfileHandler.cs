using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Application.Validation;
using Cortexa.Identity.Domain.Enums;
using Cortexa.Identity.Domain.Exceptions;

namespace Cortexa.Identity.Application.Handlers;

public sealed class UpdateProfileHandler
{
    private readonly IUserRepository _userRepo;
    private readonly IAuditWriter _auditWriter;

    public UpdateProfileHandler(IUserRepository userRepo, IAuditWriter auditWriter)
    {
        _userRepo = userRepo;
        _auditWriter = auditWriter;
    }

    public async Task<UserResponse> HandleAsync(
        Guid userId,
        Guid orgId,
        UpdateProfileRequest request,
        CancellationToken ct)
    {
        UserFieldValidator.EnsureValidEmail(request.Email);
        UserFieldValidator.EnsureValidUsername(request.Username);

        var user = await _userRepo.GetTrackedByIdAsync(userId, ct);
        if (user is null)
            throw new ForbiddenException("Access denied.");

        await EnsureEmailNotTakenAsync(request.Email, orgId, userId, ct);
        await EnsureUsernameNotTakenAsync(request.Username, userId, ct);

        var changedFields = DetermineChangedFields(user.Email, request.Email, user.Username, request.Username);

        user.UpdateProfile(request.Email, request.Username);
        await _userRepo.SaveChangesAsync(ct);

        await _auditWriter.LogAsync(
            AuditEventType.UserUpdated,
            "User",
            user.Id.ToString(),
            "update-profile",
            new { changedFields },
            ct);

        return UserResponse.From(user);
    }

    private static List<string> DetermineChangedFields(
        string previousEmail, string newEmail, string previousUsername, string newUsername)
    {
        var changed = new List<string>();
        if (!string.Equals(previousEmail, newEmail, StringComparison.Ordinal))
            changed.Add("Email");
        if (!string.Equals(previousUsername, newUsername, StringComparison.Ordinal))
            changed.Add("Username");
        return changed;
    }

    private async Task EnsureEmailNotTakenAsync(string email, Guid orgId, Guid excludeUserId, CancellationToken ct)
    {
        if (await _userRepo.EmailExistsInOrgExcludingUserAsync(email, orgId, excludeUserId, ct))
            throw new BadRequestException("Email already exists in this organization");
    }

    private async Task EnsureUsernameNotTakenAsync(string username, Guid excludeUserId, CancellationToken ct)
    {
        if (await _userRepo.UsernameExistsExcludingUserAsync(username, excludeUserId, ct))
            throw new BadRequestException("Username already taken");
    }
}
