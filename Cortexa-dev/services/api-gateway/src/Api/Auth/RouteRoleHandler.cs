using Microsoft.AspNetCore.Authorization;
using Yarp.ReverseProxy.Configuration;

namespace Cortexa.ApiGateway.Api.Auth;

public sealed class RouteRoleHandler : AuthorizationHandler<RouteRoleRequirement>
{
    private const string SuperAdminRole = "SuperAdmin";
    private const string PermissionsClaimType = "perms";

    private readonly IProxyConfigProvider _configProvider;

    public RouteRoleHandler(IProxyConfigProvider configProvider)
    {
        _configProvider = configProvider;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RouteRoleRequirement requirement)
    {
        if (context.Resource is not HttpContext httpContext)
        {
            return Task.CompletedTask;
        }

        if (IsAuthorizationSkipped(httpContext))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        if (context.User.IsInRole(SuperAdminRole))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var routeConfig = FindMatchingRoute(httpContext.Request.Path.Value ?? string.Empty, httpContext.Request.Method);
        if (routeConfig is null)
        {
            context.Fail();
            return Task.CompletedTask;
        }

        EvaluateRoutePolicy(context, requirement, routeConfig);
        return Task.CompletedTask;
    }

    private static bool IsAuthorizationSkipped(HttpContext httpContext)
        => httpContext.Items.TryGetValue("SkipAuthorization", out var skip) && skip is true;

    private static void EvaluateRoutePolicy(
        AuthorizationHandlerContext context,
        RouteRoleRequirement requirement,
        RouteConfig routeConfig)
    {
        var requiredRole = GetMetadataValue(routeConfig, "RequiredRole");
        var requiredPermission = GetMetadataValue(routeConfig, "RequiredPermission");

        if (requiredRole is null && requiredPermission is null)
        {
            if (IsAuthenticatedOnlyRoute(routeConfig))
            {
                context.Succeed(requirement);
                return;
            }

            context.Fail();
            return;
        }

        var roleSatisfied = requiredRole is null || context.User.IsInRole(requiredRole);
        var permissionSatisfied = requiredPermission is null || HasPermission(context, requiredPermission);

        if (roleSatisfied && permissionSatisfied)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }
    }

    private static bool IsAuthenticatedOnlyRoute(RouteConfig routeConfig)
        => string.Equals(GetMetadataValue(routeConfig, "RequireAuthenticatedOnly"), "true", StringComparison.OrdinalIgnoreCase);

    private static bool HasPermission(AuthorizationHandlerContext context, string requiredPermission)
        => context.User.FindAll(PermissionsClaimType)
            .Any(c => string.Equals(c.Value, requiredPermission, StringComparison.Ordinal));

    private static string? GetMetadataValue(RouteConfig routeConfig, string key)
    {
        if (routeConfig.Metadata?.TryGetValue(key, out var value) == true && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
        return null;
    }

    private RouteConfig? FindMatchingRoute(string path, string method)
    {
        var config = _configProvider.GetConfig();
        RouteConfig? anyMethodFallback = null;

        foreach (var route in config.Routes)
        {
            if (route.Match.Path is null || !RouteMatchingHelper.PathMatches(path, route.Match.Path))
            {
                continue;
            }

            if (!RouteMatchingHelper.MethodMatches(method, route.Match.Methods))
            {
                continue;
            }

            if (route.Match.Methods is { Count: > 0 })
            {
                return route;
            }

            anyMethodFallback ??= route;
        }

        return anyMethodFallback;
    }
}
