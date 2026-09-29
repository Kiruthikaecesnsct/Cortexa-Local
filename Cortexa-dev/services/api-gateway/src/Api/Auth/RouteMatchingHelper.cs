namespace Cortexa.ApiGateway.Api.Auth;

public static class RouteMatchingHelper
{
    public static bool PathMatches(string requestPath, string routePattern)
    {
        var isCatchAll = routePattern.Contains("{**catch-all}");
        var pattern = routePattern.Replace("{**catch-all}", "").TrimEnd('/');
        var path = requestPath.TrimEnd('/');

        // Exact routes (e.g. /ingestion/health) must match the whole path only, so an
        // anonymous health route never covers /ingestion/health/secret. Catch-all routes
        // (e.g. /ingestion/{**catch-all}) match the prefix and any sub-path.
        if (!isCatchAll)
        {
            return path.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        }

        if (!path.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return path.Length == pattern.Length || path[pattern.Length] == '/';
    }

    public static bool MethodMatches(string requestMethod, IReadOnlyList<string>? routeMethods)
    {
        // YARP routes with no Methods configured match every HTTP method.
        if (routeMethods is null || routeMethods.Count == 0)
        {
            return true;
        }
        return routeMethods.Any(method => string.Equals(method, requestMethod, StringComparison.OrdinalIgnoreCase));
    }
}
