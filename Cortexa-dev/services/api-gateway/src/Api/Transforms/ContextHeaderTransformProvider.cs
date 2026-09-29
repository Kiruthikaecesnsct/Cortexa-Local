using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Cortexa.ApiGateway.Api.Transforms;

public sealed class ContextHeaderTransformProvider : ITransformProvider
{
    private const string OrgIdHeader = "X-Org-Id";
    private const string UserIdHeader = "X-User-Id";
    private const string UserEmailHeader = "X-User-Email";

    public void ValidateRoute(TransformRouteValidationContext context)
    {
    }

    public void ValidateCluster(TransformClusterValidationContext context)
    {
    }

    public void Apply(TransformBuilderContext context)
    {
        context.AddRequestTransform(transformContext =>
        {
            StripSpoofableHeaders(transformContext.ProxyRequest.Headers);
            ApplyIdentityHeaders(transformContext);
            return ValueTask.CompletedTask;
        });
    }

    private static void StripSpoofableHeaders(System.Net.Http.Headers.HttpRequestHeaders headers)
    {
        headers.Remove(OrgIdHeader);
        headers.Remove(UserIdHeader);
        headers.Remove(UserEmailHeader);
    }

    private static void ApplyIdentityHeaders(RequestTransformContext transformContext)
    {
        var user = transformContext.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return;
        }

        SetHeaderFromClaim(transformContext, UserIdHeader, user.FindFirst("sub")?.Value);
        SetHeaderFromClaim(transformContext, UserEmailHeader, user.FindFirst("email")?.Value);
        SetHeaderFromClaim(transformContext, OrgIdHeader, user.FindFirst("org_id")?.Value);
    }

    private static void SetHeaderFromClaim(RequestTransformContext transformContext, string headerName, string? claimValue)
    {
        if (string.IsNullOrWhiteSpace(claimValue))
        {
            return;
        }
        transformContext.ProxyRequest.Headers.Remove(headerName);
        transformContext.ProxyRequest.Headers.Add(headerName, claimValue);
    }
}
