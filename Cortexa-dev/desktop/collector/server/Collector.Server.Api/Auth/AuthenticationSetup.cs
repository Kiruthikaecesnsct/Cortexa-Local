using System.Security.Claims;
using System.Text;
using Collector.Server.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Collector.Server.Api.Auth;

public static class AuthenticationSetup
{
    private const string AllowedAlgorithm = SecurityAlgorithms.HmacSha256;

    public static IServiceCollection AddCollectorAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<IdentityOptions>>((bearer, identity) => ConfigureBearer(bearer, identity.Value));
        services.AddAuthorizationBuilder()
            .AddPolicy(CollectorPolicies.JobsSubmit, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => CanSubmitJobs(context.User)));
        return services;
    }

    private static void ConfigureBearer(JwtBearerOptions bearer, IdentityOptions identity)
    {
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidIssuer = identity.Issuer,
            ValidAudience = identity.Audience,
            ValidAlgorithms = [AllowedAlgorithm],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(identity.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(identity.ClockSkewSeconds)
        };
    }

    private static bool CanSubmitJobs(ClaimsPrincipal user) =>
        CollectorClaims.TryGetCaller(user, out _)
        && CollectorClaims.HasPermission(user, CollectorClaims.JobsSubmit);
}
