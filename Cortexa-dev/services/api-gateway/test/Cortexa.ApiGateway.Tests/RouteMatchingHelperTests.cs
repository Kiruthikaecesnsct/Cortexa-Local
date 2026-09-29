using Cortexa.ApiGateway.Api.Auth;
using Xunit;

namespace Cortexa.ApiGateway.Tests;

public sealed class RouteMatchingHelperTests
{
    [Theory]
    [InlineData("/ingestion/health", "/ingestion/health", true)]
    [InlineData("/ingestion/health/", "/ingestion/health", true)]
    // Exact health route must NOT cover sub-paths or sibling paths — otherwise an
    // authenticated route could be reached anonymously (BUG042 regression guard).
    [InlineData("/ingestion/health/secret", "/ingestion/health", false)]
    [InlineData("/ingestion/healthcheck", "/ingestion/health", false)]
    [InlineData("/ingestion/score", "/ingestion/health", false)]
    public void ExactRoute_MatchesWholePathOnly(string requestPath, string routePattern, bool expected)
    {
        Assert.Equal(expected, RouteMatchingHelper.PathMatches(requestPath, routePattern));
    }

    [Theory]
    [InlineData("/ingestion", "/ingestion/{**catch-all}", true)]
    [InlineData("/ingestion/anything", "/ingestion/{**catch-all}", true)]
    [InlineData("/ingestion/health/secret", "/ingestion/{**catch-all}", true)]
    [InlineData("/ingestionx", "/ingestion/{**catch-all}", false)]
    [InlineData("/extraction/run", "/ingestion/{**catch-all}", false)]
    public void CatchAllRoute_MatchesPrefixAndSubPaths(string requestPath, string routePattern, bool expected)
    {
        Assert.Equal(expected, RouteMatchingHelper.PathMatches(requestPath, routePattern));
    }
}
