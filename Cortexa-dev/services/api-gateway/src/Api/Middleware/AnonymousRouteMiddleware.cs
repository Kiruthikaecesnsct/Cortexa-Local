using Cortexa.ApiGateway.Api.Auth;
using Yarp.ReverseProxy.Configuration;

namespace Cortexa.ApiGateway.Api.Middleware;

public sealed class AnonymousRouteMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IProxyConfigProvider _configProvider;

    public AnonymousRouteMiddleware(RequestDelegate next, IProxyConfigProvider configProvider)
    {
        _next = next;
        _configProvider = configProvider;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var route = FindMatchingRoute(path);

        if (route is not null)
        {
            var isAnonymous = route.Metadata?.TryGetValue("AllowAnonymous", out var allowAnon) == true
                && string.Equals(allowAnon, "true", StringComparison.OrdinalIgnoreCase);

            if (isAnonymous)
            {
                context.Items["SkipAuthorization"] = true;
            }
        }

        await _next(context);
    }

    private RouteConfig? FindMatchingRoute(string path)
    {
        var config = _configProvider.GetConfig();
        var matchingRoutes = config.Routes
            .Where(r => r.Match.Path is not null && RouteMatchingHelper.PathMatches(path, r.Match.Path))
            .OrderBy(r => r.Order ?? int.MaxValue)
            .ThenBy(r => r.Match.Path?.Contains("{**catch-all}") == true ? 1 : 0)
            .ToList();

        return matchingRoutes.FirstOrDefault();
    }
}
