using System.Net;
using Collector.Infrastructure.Remote.RateLimit;
using Collector.Tests.Support;

namespace Collector.Tests.Remote.RateLimit;

public class RateHeaderReaderTests
{
    private const int RemainingCalls = 42;
    private const long ResetEpochSeconds = 1_800_000_000;
    private const int RetryAfterSeconds = 30;
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-07T00:00:00Z");

    [Fact]
    public void Read_GitHubHeaders_ParsesRemainingAndReset()
    {
        var response = RemoteResponses.Status(
            HttpStatusCode.OK,
            ("x-ratelimit-remaining", RemainingCalls.ToString()),
            ("x-ratelimit-reset", ResetEpochSeconds.ToString()));

        var snapshot = new GitHubRateHeaders().Read(response, Now);

        Assert.Equal(RemainingCalls, snapshot.Remaining);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(ResetEpochSeconds), snapshot.ResetAt);
    }

    [Fact]
    public void Read_AzureDevOpsHeaders_ParsesRemainingAndReset()
    {
        var response = RemoteResponses.Status(
            HttpStatusCode.OK,
            ("X-RateLimit-Remaining", RemainingCalls.ToString()),
            ("X-RateLimit-Reset", ResetEpochSeconds.ToString()));

        var snapshot = new AzureDevOpsRateHeaders().Read(response, Now);

        Assert.Equal(RemainingCalls, snapshot.Remaining);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(ResetEpochSeconds), snapshot.ResetAt);
    }

    [Theory]
    [InlineData("abc", "xyz")]
    [InlineData("-5", "-1")]
    [InlineData("", "0")]
    public void Read_MalformedValues_ReturnsNulls(string remaining, string reset)
    {
        var response = RemoteResponses.Status(
            HttpStatusCode.OK,
            ("x-ratelimit-remaining", remaining),
            ("x-ratelimit-reset", reset));

        var snapshot = new GitHubRateHeaders().Read(response, Now);

        Assert.Null(snapshot.Remaining);
        Assert.Null(snapshot.ResetAt);
    }

    [Fact]
    public void Read_NoHeaders_ReturnsEmptySnapshot()
    {
        var snapshot = new GitHubRateHeaders().Read(RemoteResponses.Status(HttpStatusCode.OK), Now);

        Assert.Equal(RateSnapshot.Empty, snapshot);
    }

    [Fact]
    public void Read_RetryAfterSeconds_ParsesDelay()
    {
        var response = RemoteResponses.RetryAfter(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(RetryAfterSeconds));

        var snapshot = new GitHubRateHeaders().Read(response, Now);

        Assert.Equal(TimeSpan.FromSeconds(RetryAfterSeconds), snapshot.RetryAfter);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public void IsRateLimited_AzureDevOps_OnlyTooManyRequests(HttpStatusCode status, bool expected)
    {
        var reader = new AzureDevOpsRateHeaders();
        var response = RemoteResponses.Status(status);

        var limited = reader.IsRateLimited(response, reader.Read(response, Now));

        Assert.Equal(expected, limited);
    }

    [Fact]
    public void IsRateLimited_GitHubTooManyRequests_ReturnsTrue()
    {
        var reader = new GitHubRateHeaders();
        var response = RemoteResponses.Status(HttpStatusCode.TooManyRequests);

        Assert.True(reader.IsRateLimited(response, reader.Read(response, Now)));
    }

    [Fact]
    public void IsRateLimited_GitHubForbiddenWithZeroRemaining_ReturnsTrue()
    {
        var reader = new GitHubRateHeaders();
        var response = RemoteResponses.Status(HttpStatusCode.Forbidden, ("x-ratelimit-remaining", "0"));

        Assert.True(reader.IsRateLimited(response, reader.Read(response, Now)));
    }

    [Fact]
    public void IsRateLimited_GitHubForbiddenWithRetryAfter_ReturnsTrue()
    {
        var reader = new GitHubRateHeaders();
        var response = RemoteResponses.RetryAfter(HttpStatusCode.Forbidden, TimeSpan.FromSeconds(RetryAfterSeconds));

        Assert.True(reader.IsRateLimited(response, reader.Read(response, Now)));
    }

    [Fact]
    public void IsRateLimited_GitHubForbiddenWithRemainingLeft_ReturnsFalse()
    {
        var reader = new GitHubRateHeaders();
        var response = RemoteResponses.Status(HttpStatusCode.Forbidden, ("x-ratelimit-remaining", RemainingCalls.ToString()));

        Assert.False(reader.IsRateLimited(response, reader.Read(response, Now)));
    }

    [Fact]
    public void IsRateLimited_GitHubForbiddenWithSsoHeader_ReturnsFalse()
    {
        var reader = new GitHubRateHeaders();
        var response = RemoteResponses.Status(
            HttpStatusCode.Forbidden,
            ("x-ratelimit-remaining", "0"),
            ("x-github-sso", "required; url=https://github.com/orgs/x/sso"));

        Assert.False(reader.IsRateLimited(response, reader.Read(response, Now)));
    }

    [Fact]
    public void IsRateLimited_GitHubOk_ReturnsFalse()
    {
        var reader = new GitHubRateHeaders();
        var response = RemoteResponses.Status(HttpStatusCode.OK, ("x-ratelimit-remaining", "0"));

        Assert.False(reader.IsRateLimited(response, reader.Read(response, Now)));
    }
}
