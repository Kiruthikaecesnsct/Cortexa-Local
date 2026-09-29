using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Enums;

namespace Cortexa.Identity.Application.Handlers;

public sealed class LogoutHandler
{
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly ITokenService _tokenService;
    private readonly IAuditWriter _auditWriter;

    public LogoutHandler(
        IRefreshTokenRepository refreshTokenRepo,
        ITokenService tokenService,
        IAuditWriter auditWriter)
    {
        _refreshTokenRepo = refreshTokenRepo;
        _tokenService = tokenService;
        _auditWriter = auditWriter;
    }

    public async Task HandleAsync(string rawRefreshToken, CancellationToken ct)
    {
        try
        {
            var tokenHash = _tokenService.ComputeTokenHash(rawRefreshToken);
            var token = await _refreshTokenRepo.GetByTokenHashAsync(tokenHash, ct);
            if (token is not null && token.IsActive)
            {
                token.MarkUsed();
                await _refreshTokenRepo.SaveChangesAsync(ct);

                await _auditWriter.LogAuthAsync(
                    AuditEventType.Logout, token.UserId, "logout", success: true, failureReason: null, ct);
                await _auditWriter.LogAuthAsync(
                    AuditEventType.TokenRevoked, token.UserId, "logout", success: true, failureReason: null, ct);
            }
        }
        catch (FormatException)
        {
        }
    }
}
