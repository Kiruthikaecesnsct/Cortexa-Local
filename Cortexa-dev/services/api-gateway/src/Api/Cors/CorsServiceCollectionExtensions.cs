namespace Cortexa.ApiGateway.Api.Cors;

public static class CorsServiceCollectionExtensions
{
    public const string PolicyName = "GatewaySpa";

    public static IServiceCollection AddGatewayCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = configuration
            .GetSection("Cors")
            .Get<CorsSettings>()
            ?? new CorsSettings();

        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                var origins = settings.AllowedOrigins;

                if (origins is null || origins.Length == 0)
                {
                    policy.WithOrigins(Array.Empty<string>());
                    return;
                }

                policy
                    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                    .WithHeaders("Authorization", "Content-Type", "X-Correlation-Id")
                    .WithExposedHeaders("X-Correlation-Id")
                    // The SPA client sends credentials (refresh cookie), so the policy
                    // must allow them. This also forces the reflected-origin path below —
                    // a literal "*" is invalid with credentials.
                    .AllowCredentials();

                // A "*" entry opens the gateway to any origin. The request origin is
                // reflected back (rather than a literal "*") so credentialed requests
                // still work; use only for dev/manual testing against deployed backends.
                if (Array.Exists(origins, origin => origin == "*"))
                {
                    policy.SetIsOriginAllowed(_ => true);
                    return;
                }

                policy.WithOrigins(origins);
            });
        });

        return services;
    }
}
