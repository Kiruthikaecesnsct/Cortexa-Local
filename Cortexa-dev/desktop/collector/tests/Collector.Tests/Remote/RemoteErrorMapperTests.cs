using System.Net;
using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Infrastructure.Remote.AzureDevOps;
using Collector.Infrastructure.Remote.Cortexa;
using Collector.Infrastructure.Remote.GitHub;
using Collector.Infrastructure.Remote.RateLimit;
using Collector.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Remote;

public class RemoteErrorMapperTests
{
    private const long ResetEpochSeconds = 1_800_000_000;
    private const int RetryAfterSeconds = 45;

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-07T00:00:00Z"));

    private GitHubErrorMapper GitHub() => new(new GitHubRateHeaders(), _time);

    private AzureDevOpsErrorMapper AzureDevOps() => new(new AzureDevOpsRateHeaders(), _time);

    private CortexaErrorMapper Cortexa() => new(_time);

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, RemoteFailureKind.Auth)]
    [InlineData(HttpStatusCode.Forbidden, RemoteFailureKind.AccessDenied)]
    [InlineData(HttpStatusCode.NotFound, RemoteFailureKind.NotFound)]
    [InlineData(HttpStatusCode.Conflict, RemoteFailureKind.EmptyRepository)]
    [InlineData(HttpStatusCode.TooManyRequests, RemoteFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, RemoteFailureKind.Upstream)]
    [InlineData(HttpStatusCode.BadGateway, RemoteFailureKind.Upstream)]
    public void GitHubMap_Status_ReturnsExpectedKind(HttpStatusCode status, RemoteFailureKind expected)
    {
        var exception = GitHub().Map(RemoteResponses.Status(status));

        Assert.Equal(expected, exception.Kind);
        Assert.Equal(SourceType.Github, exception.Provider);
    }

    [Fact]
    public void GitHubMap_ForbiddenWithSsoHeader_ReturnsSsoRequired()
    {
        var response = RemoteResponses.Status(HttpStatusCode.Forbidden, ("x-github-sso", "required; url=https://github.com/sso"));

        Assert.Equal(RemoteFailureKind.SsoRequired, GitHub().Map(response).Kind);
    }

    [Fact]
    public void GitHubMap_ForbiddenWithSsoAndZeroRemaining_ReturnsSsoRequired()
    {
        var response = RemoteResponses.Status(
            HttpStatusCode.Forbidden,
            ("x-github-sso", "required"),
            ("x-ratelimit-remaining", "0"));

        Assert.Equal(RemoteFailureKind.SsoRequired, GitHub().Map(response).Kind);
    }

    [Fact]
    public void GitHubMap_ForbiddenWithZeroRemaining_ReturnsRateLimitedWithResetTime()
    {
        var response = RemoteResponses.Status(
            HttpStatusCode.Forbidden,
            ("x-ratelimit-remaining", "0"),
            ("x-ratelimit-reset", ResetEpochSeconds.ToString()));

        var exception = GitHub().Map(response);

        Assert.Equal(RemoteFailureKind.RateLimited, exception.Kind);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(ResetEpochSeconds), exception.ResetAt);
    }

    [Fact]
    public void GitHubMap_ForbiddenWithRemainingLeft_ReturnsAccessDenied()
    {
        var response = RemoteResponses.Status(HttpStatusCode.Forbidden, ("x-ratelimit-remaining", "100"));

        Assert.Equal(RemoteFailureKind.AccessDenied, GitHub().Map(response).Kind);
    }

    [Fact]
    public void GitHubMap_TooManyRequestsWithRetryAfter_ResetsRelativeToNow()
    {
        var response = RemoteResponses.RetryAfter(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(RetryAfterSeconds));

        var exception = GitHub().Map(response);

        Assert.Equal(_time.GetUtcNow().AddSeconds(RetryAfterSeconds), exception.ResetAt);
    }

    [Fact]
    public void GitHubMap_TooManyRequestsWithoutHeaders_HasNoResetTime()
    {
        var exception = GitHub().Map(RemoteResponses.Status(HttpStatusCode.TooManyRequests));

        Assert.Null(exception.ResetAt);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Found, false)]
    public void GitHubIsSuccess_Status_MatchesSuccessRange(HttpStatusCode status, bool expected)
    {
        Assert.Equal(expected, GitHub().IsSuccess(RemoteResponses.Status(status)));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, RemoteFailureKind.Auth)]
    [InlineData(HttpStatusCode.NonAuthoritativeInformation, RemoteFailureKind.Auth)]
    [InlineData(HttpStatusCode.MultipleChoices, RemoteFailureKind.Auth)]
    [InlineData(HttpStatusCode.Found, RemoteFailureKind.Auth)]
    [InlineData(HttpStatusCode.TemporaryRedirect, RemoteFailureKind.Auth)]
    [InlineData(HttpStatusCode.Forbidden, RemoteFailureKind.AccessDenied)]
    [InlineData(HttpStatusCode.NotFound, RemoteFailureKind.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, RemoteFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, RemoteFailureKind.Upstream)]
    [InlineData(HttpStatusCode.Conflict, RemoteFailureKind.Upstream)]
    public void AzureDevOpsMap_Status_ReturnsExpectedKind(HttpStatusCode status, RemoteFailureKind expected)
    {
        var exception = AzureDevOps().Map(RemoteResponses.Status(status));

        Assert.Equal(expected, exception.Kind);
        Assert.Equal(SourceType.AzureDevops, exception.Provider);
    }

    [Fact]
    public void AzureDevOpsMap_TooManyRequestsWithReset_ReturnsResetTime()
    {
        var response = RemoteResponses.Status(
            HttpStatusCode.TooManyRequests,
            ("X-RateLimit-Reset", ResetEpochSeconds.ToString()));

        var exception = AzureDevOps().Map(response);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(ResetEpochSeconds), exception.ResetAt);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NonAuthoritativeInformation, false)]
    [InlineData(HttpStatusCode.Found, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public void AzureDevOpsIsSuccess_Status_TreatsSignInPageAsFailure(HttpStatusCode status, bool expected)
    {
        Assert.Equal(expected, AzureDevOps().IsSuccess(RemoteResponses.Status(status)));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, RemoteFailureKind.Auth)]
    [InlineData(HttpStatusCode.Forbidden, RemoteFailureKind.AccessDenied)]
    [InlineData(HttpStatusCode.NotFound, RemoteFailureKind.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, RemoteFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, RemoteFailureKind.Upstream)]
    [InlineData(HttpStatusCode.BadGateway, RemoteFailureKind.Upstream)]
    [InlineData(HttpStatusCode.Conflict, RemoteFailureKind.Upstream)]
    public void CortexaMap_Status_ReturnsExpectedKind(HttpStatusCode status, RemoteFailureKind expected)
    {
        var exception = Cortexa().Map(RemoteResponses.Status(status));

        Assert.Equal(expected, exception.Kind);
        Assert.Equal(SourceType.CortexaRepo, exception.Provider);
    }

    [Fact]
    public void CortexaMap_TooManyRequestsWithRetryAfterDelay_ResetsRelativeToNow()
    {
        var response = RemoteResponses.RetryAfter(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(RetryAfterSeconds));

        var exception = Cortexa().Map(response);

        Assert.Equal(_time.GetUtcNow().AddSeconds(RetryAfterSeconds), exception.ResetAt);
    }

    [Fact]
    public void CortexaMap_TooManyRequestsWithRetryAfterDate_UsesThatDate()
    {
        var resetAt = DateTimeOffset.FromUnixTimeSeconds(ResetEpochSeconds);
        var response = RemoteResponses.Status(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(resetAt);

        var exception = Cortexa().Map(response);

        Assert.Equal(resetAt, exception.ResetAt);
    }

    [Fact]
    public void CortexaMap_TooManyRequestsWithoutHeader_HasNoResetTime()
    {
        var exception = Cortexa().Map(RemoteResponses.Status(HttpStatusCode.TooManyRequests));

        Assert.Null(exception.ResetAt);
    }

    [Fact]
    public void CortexaMap_NonRateLimitedStatus_IgnoresRetryAfter()
    {
        var response = RemoteResponses.RetryAfter(HttpStatusCode.ServiceUnavailable, TimeSpan.FromSeconds(RetryAfterSeconds));

        Assert.Null(Cortexa().Map(response).ResetAt);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.Found, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public void CortexaIsSuccess_Status_MatchesSuccessRange(HttpStatusCode status, bool expected)
    {
        Assert.Equal(expected, Cortexa().IsSuccess(RemoteResponses.Status(status)));
    }
}
