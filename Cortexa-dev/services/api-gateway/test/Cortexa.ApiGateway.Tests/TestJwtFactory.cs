using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Cortexa.ApiGateway.Tests;

public static class TestJwtFactory
{
    private const string TestSigningKey = "dev-symmetric-key-min-256-bits-long-shared-with-identity-service-for-local-development-only";
    private const string TestIssuer = "cortexa-identity-dev";
    private const string TestAudience = "cortexa-dev";

    public static readonly Guid DefaultSecurityStamp = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static string CreateValidToken(
        string? role = null,
        IReadOnlyCollection<string>? perms = null,
        Guid? orgId = null,
        Guid? stamp = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "test-user-id"),
            new(JwtRegisteredClaimNames.Email, "test@cortexa.local"),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
            new("stamp", (stamp ?? DefaultSecurityStamp).ToString())
        };

        if (!string.IsNullOrWhiteSpace(role))
        {
            claims.Add(new Claim("role", role));
        }

        if (perms is { Count: > 0 })
        {
            claims.Add(new Claim("perms", JsonSerializer.Serialize(perms), JsonClaimValueTypes.JsonArray));
        }

        if (orgId is { } organizationId)
        {
            claims.Add(new Claim("org_id", organizationId.ToString()));
        }

        return CreateToken(claims, DateTime.UtcNow.AddMinutes(60));
    }

    public static string CreateValidTokenWithoutStampClaim()
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "test-user-id"),
            new(JwtRegisteredClaimNames.Email, "test@cortexa.local"),
            new("perms", JsonSerializer.Serialize(new[] { "documents:write" }), JsonClaimValueTypes.JsonArray)
        };

        return CreateToken(claims, DateTime.UtcNow.AddMinutes(60));
    }

    public static string CreateExpiredToken()
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "test-user-id"),
            new(JwtRegisteredClaimNames.Email, "test@cortexa.local")
        };

        var expiry = DateTime.UtcNow.AddMinutes(-10);
        var notBefore = expiry.AddMinutes(-60);
        return CreateToken(claims, notBefore, expiry);
    }

    public static string CreateTamperedToken()
    {
        var validToken = CreateValidToken();
        var parts = validToken.Split('.');
        if (parts.Length != 3)
        {
            return validToken;
        }

        var tamperedPayload = parts[1] + "tampered";
        return $"{parts[0]}.{tamperedPayload}.{parts[2]}";
    }

    private static string CreateToken(IEnumerable<Claim> claims, DateTime expires)
    {
        return CreateToken(claims, DateTime.UtcNow, expires);
    }

    private static string CreateToken(IEnumerable<Claim> claims, DateTime notBefore, DateTime expires)
    {
        var keyBytes = Encoding.UTF8.GetBytes(TestSigningKey);
        var securityKey = new SymmetricSecurityKey(keyBytes);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            notBefore: notBefore,
            expires: expires,
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
