using System.Text;
using Collector.Server.Api.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Collector.Server.Tests.Api;

public sealed record TokenSpec
{
    public string? Subject { get; init; } = TestIdentity.UserId;

    public string? OrgId { get; init; } = TestIdentity.OrgId;

    public string[]? Permissions { get; init; } = [CollectorClaims.JobsSubmit];

    public Guid? Stamp { get; init; } = TestIdentity.Stamp;

    public string Issuer { get; init; } = TestSettings.Issuer;

    public string Audience { get; init; } = TestSettings.Audience;

    public string SigningKey { get; init; } = TestSettings.SigningKey;

    public string Algorithm { get; init; } = SecurityAlgorithms.HmacSha256;

    public TimeSpan ExpiresIn { get; init; } = TimeSpan.FromHours(1);
}

internal static class TestJwtFactory
{
    private const string PlainJwtHeader = "{\"alg\":\"none\",\"typ\":\"JWT\"}";
    private static readonly TimeSpan ValidityWindow = TimeSpan.FromHours(2);

    public static string Create(TokenSpec? spec = null)
    {
        var effective = spec ?? new TokenSpec();
        var expires = DateTime.UtcNow.Add(effective.ExpiresIn);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = effective.Issuer,
            Audience = effective.Audience,
            Expires = expires,
            NotBefore = expires - ValidityWindow,
            Claims = BuildClaims(effective),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(effective.SigningKey)),
                effective.Algorithm)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public static string CreateUnsigned(TokenSpec? spec = null)
    {
        var effective = spec ?? new TokenSpec();
        var claims = BuildClaims(effective);
        claims["iss"] = effective.Issuer;
        claims["aud"] = effective.Audience;
        claims["exp"] = DateTimeOffset.UtcNow.Add(effective.ExpiresIn).ToUnixTimeSeconds();
        var payload = System.Text.Json.JsonSerializer.Serialize(claims);
        return $"{Encode(PlainJwtHeader)}.{Encode(payload)}.";
    }

    private static Dictionary<string, object> BuildClaims(TokenSpec spec)
    {
        var claims = new Dictionary<string, object>();
        AddIfPresent(claims, CollectorClaims.Subject, spec.Subject);
        AddIfPresent(claims, CollectorClaims.OrgId, spec.OrgId);
        AddIfPresent(claims, CollectorClaims.Stamp, spec.Stamp?.ToString());
        AddIfPresent(claims, CollectorClaims.Permissions, spec.Permissions);
        return claims;
    }

    private static void AddIfPresent(Dictionary<string, object> claims, string name, object? value)
    {
        if (value is not null)
        {
            claims[name] = value;
        }
    }

    private static string Encode(string value) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(value));
}
