using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Cortexa.Identity.Application.Handlers;
using Cortexa.Identity.Application.Interfaces;
using Cortexa.Identity.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Cortexa.Identity.Infrastructure.Services;

public sealed class EntraTokenValidator : IEntraTokenValidator
{
    private readonly EntraIdSettings _settings;
    private readonly IConfigurationManager<OpenIdConnectConfiguration> _configManager;

    public EntraTokenValidator(IOptions<EntraIdSettings> settings)
    {
        _settings = settings.Value;
        _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            _settings.MetadataAddress,
            new OpenIdConnectConfigurationRetriever());
    }

    public async Task<EntraClaims> ValidateAsync(string idToken, CancellationToken ct)
    {
        var config = await _configManager.GetConfigurationAsync(ct);

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _settings.ValidIssuer,
            ValidateAudience = true,
            ValidAudience = _settings.Audience,
            ValidateLifetime = true,
            IssuerSigningKeys = config.SigningKeys,
            ValidateIssuerSigningKey = true
        };

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(idToken, validationParameters, out _);
            return ExtractClaims(principal);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            throw new UnauthorizedException();
        }
    }

    private static EntraClaims ExtractClaims(ClaimsPrincipal principal)
    {
        var email = principal.FindFirst("email")?.Value
            ?? principal.FindFirst("preferred_username")?.Value
            ?? principal.FindFirst("upn")?.Value
            ?? throw new UnauthorizedException();

        var name = principal.FindFirst("name")?.Value
            ?? throw new UnauthorizedException();

        var oid = principal.FindFirst("oid")?.Value
            ?? principal.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? throw new UnauthorizedException();

        var groupIds = principal.FindAll("groups")
            .Select(c => c.Value)
            .ToList();

        return new EntraClaims(email, name, oid, groupIds);
    }
}
