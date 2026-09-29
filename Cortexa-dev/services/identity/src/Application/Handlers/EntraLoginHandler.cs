using Cortexa.Identity.Application.DTOs;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Domain.Enums;

namespace Cortexa.Identity.Application.Handlers;

public sealed class EntraLoginHandler
{
    private readonly IEntraTokenValidator _tokenValidator;
    private readonly IEntraGroupMapper _groupMapper;
    private readonly IUserRepository _userRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly ITokenService _tokenService;
    private readonly IPermissionRepository _permissionRepo;
    private readonly IAuditWriter _auditWriter;

    public EntraLoginHandler(
        IEntraTokenValidator tokenValidator,
        IEntraGroupMapper groupMapper,
        IUserRepository userRepo,
        IRefreshTokenRepository refreshTokenRepo,
        ITokenService tokenService,
        IPermissionRepository permissionRepo,
        IAuditWriter auditWriter)
    {
        _tokenValidator = tokenValidator;
        _groupMapper = groupMapper;
        _userRepo = userRepo;
        _refreshTokenRepo = refreshTokenRepo;
        _tokenService = tokenService;
        _permissionRepo = permissionRepo;
        _auditWriter = auditWriter;
    }

    public async Task<(LoginResponse Response, string RawRefreshToken)> HandleAsync(
        EntraLoginRequest request, CancellationToken ct)
    {
        try
        {
            var claims = await _tokenValidator.ValidateAsync(request.IdToken, ct);
            var assignment = _groupMapper.Map(claims.GroupIds);
            var user = await ResolveUserAsync(claims, assignment, ct);
            var result = await IssueTokensAsync(user, ct);

            await _auditWriter.LogAuthAsync(
                AuditEventType.LoginSucceeded, user.Id, "sso-login", success: true, failureReason: null, ct);

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _auditWriter.LogAuthAsync(
                AuditEventType.LoginFailed, null, "sso-login", success: false, "sso-validation-failed", ct);
            throw;
        }
    }

    private async Task<User> ResolveUserAsync(EntraClaims claims, EntraAssignment assignment, CancellationToken ct)
    {
        var existing = await _userRepo.GetByEmailAsync(claims.Email, ct);
        if (existing is not null)
            return await ReconcileExistingUserAsync(existing, assignment, ct);

        var username = DeriveUsername(claims.Email, claims.EntraObjectId);
        var user = User.CreateFromEntra(
            claims.Email, username, claims.EntraObjectId, assignment.Role, assignment.OrganizationId);
        await _userRepo.AddAsync(user, ct);
        return user;
    }

    private async Task<User> ReconcileExistingUserAsync(User existing, EntraAssignment assignment, CancellationToken ct)
    {
        if (existing.IsSystem || !HasMappingChanged(existing, assignment))
            return existing;

        var tracked = await _userRepo.GetTrackedByEmailAsync(existing.Email, ct);
        if (tracked is null || tracked.IsSystem)
            return existing;

        tracked.ApplyEntraMapping(assignment.Role, assignment.OrganizationId);
        await _userRepo.SaveChangesAsync(ct);
        return tracked;
    }

    private static bool HasMappingChanged(User user, EntraAssignment assignment)
        => user.Role != assignment.Role || user.OrganizationId != assignment.OrganizationId;

    private async Task<(LoginResponse, string)> IssueTokensAsync(User user, CancellationToken ct)
    {
        var permissions = await _permissionRepo.GetPermissionNamesForUserAsync(user.Role, ct);
        var (accessToken, expiresAt) = _tokenService.GenerateAccessToken(user, permissions);
        var (rawToken, tokenHash) = _tokenService.GenerateRefreshToken();

        var refreshToken = RefreshToken.Create(user.Id, tokenHash, DateTimeOffset.UtcNow.AddDays(7));
        await _refreshTokenRepo.AddAsync(refreshToken, ct);

        return (new LoginResponse(accessToken, expiresAt), rawToken);
    }

    private static string DeriveUsername(string email, string oid)
    {
        const int MaxUsernameLength = 100;
        var localPart = email.Split('@')[0];
        var maxLocalPartLength = MaxUsernameLength - oid.Length - 1;

        if (localPart.Length > maxLocalPartLength)
            localPart = localPart[..maxLocalPartLength];

        return $"{localPart}_{oid}";
    }
}
