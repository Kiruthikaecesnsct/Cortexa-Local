using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Cortexa.ApiGateway.Api.Auth;

public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtSettings = configuration
            .GetSection("Jwt")
            .Get<JwtSettings>()
            ?? throw new InvalidOperationException("Jwt configuration section is missing");

        if (string.IsNullOrWhiteSpace(jwtSettings.SigningKey))
        {
            throw new InvalidOperationException("Jwt:SigningKey is required");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var signingKeyBytes = Encoding.UTF8.GetBytes(jwtSettings.SigningKey);
                var securityKey = new SymmetricSecurityKey(signingKeyBytes);

                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    IssuerSigningKey = securityKey,
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        return services;
    }
}
