using Cortexa.Identity.Domain.Entities;

namespace Cortexa.Identity.Application.Interfaces;

public interface ITokenService
{
    (string Token, DateTimeOffset ExpiresAt) GenerateAccessToken(User user, IReadOnlyCollection<string> permissions);
    (string RawToken, string TokenHash) GenerateRefreshToken();
    string ComputeTokenHash(string rawToken);
}
