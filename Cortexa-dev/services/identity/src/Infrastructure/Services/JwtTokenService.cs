using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Domain.Entities;
using Cortexa.Identity.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Cortexa.Identity.Infrastructure.Services;

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtSettings _settings;

    public JwtTokenService(IOptions<JwtSettings> settings)
    {
        _settings = settings.Value;
    }

    public (string Token, DateTimeOffset ExpiresAt) GenerateAccessToken(User user, IReadOnlyCollection<string> permissions)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_settings.AccessTokenExpiryMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("role", user.Role.ToString()),
            new("roles", JsonSerializer.Serialize(new[] { user.Role.ToString() }), JsonClaimValueTypes.JsonArray),
            new("perms", JsonSerializer.Serialize(permissions), JsonClaimValueTypes.JsonArray),
            new("stamp", user.SecurityStamp.ToString()),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        if (user.OrganizationId is { } organizationId)
            claims.Add(new Claim("org_id", organizationId.ToString()));

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public (string RawToken, string TokenHash) GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Base64UrlEncoder.Encode(bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return (rawToken, hash);
    }

    public string ComputeTokenHash(string rawToken)
    {
        var bytes = Base64UrlEncoder.DecodeBytes(rawToken);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return hash;
    }
}
