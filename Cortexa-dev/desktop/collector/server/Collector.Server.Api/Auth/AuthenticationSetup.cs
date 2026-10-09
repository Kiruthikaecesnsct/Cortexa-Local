using System.Security.Claims;
using System.Text;
using Collector.Server.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Collector.Server.Api.Auth;

public static class AuthenticationSetup
{
    private const string AllowedAlgorithm = SecurityAlgorithms.HmacSha256;
    private const string LoggerCategory = "Collector.Server.Api.Auth.CollectorAuthentication";

    private const string SigningKeyMismatchMessage =
        "JWT rejected: token signature does not match the configured Identity:SigningKey. " +
        "This is the US146 key-drift symptom - Collector Server's signing key is out of sync " +
        "with the key the native identity/api-gateway services used to issue this token. " +
        "Compare signing-key fingerprints between the two services to confirm.";

    private const string OtherAuthenticationFailureMessage =
        "JWT rejected for a reason other than signature/key mismatch (expired, wrong issuer, " +
        "wrong audience, or malformed token).";

    public static IServiceCollection AddCollectorAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<IdentityOptions>, ILoggerFactory>(
                (bearer, identity, loggerFactory) => ConfigureBearer(bearer, identity.Value, loggerFactory));
        services.AddAuthorizationBuilder()
            .AddPolicy(CollectorPolicies.JobsSubmit, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => CanSubmitJobs(context.User)));
        return services;
    }

    private static void ConfigureBearer(JwtBearerOptions bearer, IdentityOptions identity, ILoggerFactory loggerFactory)
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
        bearer.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context => LogAuthenticationFailed(context, loggerFactory)
        };
    }

    private static Task LogAuthenticationFailed(AuthenticationFailedContext context, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(LoggerCategory);
        if (IsSigningKeyMismatch(context.Exception))
        {
            logger.LogWarning(context.Exception, SigningKeyMismatchMessage);
        }
        else
        {
            logger.LogDebug(context.Exception, OtherAuthenticationFailureMessage);
        }

        return Task.CompletedTask;
    }

    private static bool IsSigningKeyMismatch(Exception? exception) =>
        exception is SecurityTokenInvalidSignatureException or SecurityTokenSignatureKeyNotFoundException;

    private static bool CanSubmitJobs(ClaimsPrincipal user) =>
        CollectorClaims.TryGetCaller(user, out _)
        && CollectorClaims.HasPermission(user, CollectorClaims.JobsSubmit);
}
