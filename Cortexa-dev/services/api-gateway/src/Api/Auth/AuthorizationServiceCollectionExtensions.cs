using Microsoft.AspNetCore.Authorization;

namespace Cortexa.ApiGateway.Api.Auth;

public static class AuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayAuthorization(
        this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, RouteRoleHandler>();

        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .AddRequirements(new RouteRoleRequirement())
                .Build();

            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .AddRequirements(new RouteRoleRequirement())
                .Build();
        });

        return services;
    }
}
