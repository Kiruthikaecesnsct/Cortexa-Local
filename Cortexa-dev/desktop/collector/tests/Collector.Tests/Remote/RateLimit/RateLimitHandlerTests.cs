using System.Net;
using System.Text;
using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Infrastructure.Options;
using Collector.Infrastructure.Remote.RateLimit;
using Collector.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Remote.RateLimit;

public sealed class RateLimitHandlerTests : IDisposable
{
    private const string TestUrl = "https://api.github.com/user";
    private const int SecondaryWaitSeconds = 60;
    private const int RetryAfterSeconds = 30;
    private const int MaxRetries = 2;
    private const int LowRemaining = 7;
    private const long ResetEpochSeconds = 1_800_000_000;
    private const int LongRetries = 10;
    private const int ThrottledAttempts = 5;
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-07T00:00:00Z"));
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    private (HttpClient Client, RateLimitGate Gate, StubHttpHandler Inner) Build(
        Queue<Func<HttpResponseMessage>> responses,
        int maxRetries = MaxRetries) =>
        BuildWith(new StubHttpHandler((_, _) => Task.FromResult(responses.Dequeue()())), maxRetries);

    private (HttpClient Client, RateLimitGate Gate, StubHttpHandler Inner) BuildWith(StubHttpHandler inner, int maxRetries)
    {
        var options = new RateLimitOptions { MaxRetries = maxRetries, SecondaryWaitSeconds = SecondaryWaitSeconds };
        var gate = new RateLimitGate(SourceType.Github, 0, options, _time);
        var handler = new RateLimitHandler(
            SourceType.Github,
            gate,
            new GitHubRateHeaders(),
            new RateLimitHandlerSettings(options, _time, AttemptTimeout, maxRetries))
        { InnerHandler = inner };
        var client = new HttpClient(handler);
        _disposables.Add(client);
        _disposables.Add(gate);
        return (client, gate, inner);
    }

    private static Queue<Func<HttpResponseMessage>> Script(params Func<HttpResponseMessage>[] responses) => new(responses);

    private static HttpResponseMessage Ok() => RemoteResponses.Status(HttpStatusCode.OK);

    private static HttpResponseMessage Limited(TimeSpan retryAfter) =>
        RemoteResponses.RetryAfter(HttpStatusCode.TooManyRequests, retryAfter);

    [Fact]
    public async Task SendAsync_SuccessfulResponse_PassesThroughAndUpdatesGate()
    {
        var (client, gate, inner) = Build(Script(() => RemoteResponses.Status(
            HttpStatusCode.OK,
            ("x-ratelimit-remaining", LowRemaining.ToString()))));

        var response = await client.GetAsync(TestUrl, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(LowRemaining, gate.Status.Remaining);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task SendAsync_RateLimitedThenOk_RetriesAndReturnsOk()
    {
        var (client, _, inner) = Build(Script(() => Limited(TimeSpan.Zero), Ok));

        var response = await client.GetAsync(TestUrl, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task SendAsync_RetryAfterHeader_WaitsBeforeRetrying()
    {
        var (client, _, inner) = Build(Script(() => Limited(TimeSpan.FromSeconds(RetryAfterSeconds)), Ok));

        var pending = client.GetAsync(TestUrl, TestSupport.Ct);

        Assert.False(pending.IsCompleted);
        Assert.Single(inner.Requests);
        _time.Advance(TimeSpan.FromSeconds(RetryAfterSeconds));
        var response = await pending.WaitAsync(Patience, TestSupport.Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_RateLimitedWithoutHeaders_WaitsSecondaryDelay()
    {
        var (client, _, _) = Build(Script(() => RemoteResponses.Status(HttpStatusCode.TooManyRequests), Ok));

        var pending = client.GetAsync(TestUrl, TestSupport.Ct);

        Assert.False(pending.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(SecondaryWaitSeconds));
        var response = await pending.WaitAsync(Patience, TestSupport.Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_RetriesExhausted_ThrowsRateLimited()
    {
        var (client, _, inner) = Build(Script(
            () => Limited(TimeSpan.Zero),
            () => Limited(TimeSpan.Zero),
            () => Limited(TimeSpan.Zero)));

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(() => client.GetAsync(TestUrl, TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.RateLimited, exception.Kind);
        Assert.Equal(SourceType.Github, exception.Provider);
        Assert.Equal(MaxRetries + 1, inner.Requests.Count);
    }

    [Fact]
    public async Task SendAsync_NoRetriesAllowed_ThrowsWithResetTime()
    {
        var (client, _, inner) = Build(
            Script(() => RemoteResponses.Status(
                HttpStatusCode.TooManyRequests,
                ("x-ratelimit-reset", ResetEpochSeconds.ToString()))),
            maxRetries: 0);

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(() => client.GetAsync(TestUrl, TestSupport.Ct));

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(ResetEpochSeconds), exception.ResetAt);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task SendAsync_ForbiddenWithZeroRemaining_IsTreatedAsRateLimited()
    {
        var (client, _, inner) = Build(Script(
            () => RemoteResponses.Status(HttpStatusCode.Forbidden, ("x-ratelimit-remaining", "0"), ("Retry-After", "0")),
            Ok));

        var response = await client.GetAsync(TestUrl, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task SendAsync_ForbiddenWithSso_IsReturnedWithoutRetry()
    {
        var (client, _, inner) = Build(Script(() => RemoteResponses.Status(
            HttpStatusCode.Forbidden,
            ("x-ratelimit-remaining", "0"),
            ("x-github-sso", "required"))));

        var response = await client.GetAsync(TestUrl, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task SendAsync_RetryAfterOnSuccess_ThrottlesNextRequest()
    {
        var (client, _, _) = Build(Script(
            () => RemoteResponses.RetryAfter(HttpStatusCode.OK, TimeSpan.FromSeconds(RetryAfterSeconds)),
            Ok));
        await client.GetAsync(TestUrl, TestSupport.Ct);

        var next = client.GetAsync(TestUrl, TestSupport.Ct);

        Assert.False(next.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(RetryAfterSeconds));
        var response = await next.WaitAsync(Patience, TestSupport.Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_RetryOfRequestWithBody_ResendsSameBody()
    {
        const string Payload = "{\"query\":\"value\"}";
        var (client, _, inner) = Build(Script(() => Limited(TimeSpan.Zero), Ok));
        using var content = new StringContent(Payload, Encoding.UTF8, "application/json");

        await client.PostAsync(TestUrl, content, TestSupport.Ct);

        Assert.Equal([Payload, Payload], inner.Requests.Select(request => request.Body));
    }

    [Fact]
    public async Task SendAsync_RetryAfterLongerThanAttemptTimeout_StillReturnsOk()
    {
        var (client, _, _) = Build(Script(() => Limited(TimeSpan.FromSeconds(RetryAfterSeconds)), Ok));
        Assert.True(TimeSpan.FromSeconds(RetryAfterSeconds) > AttemptTimeout);

        var pending = client.GetAsync(TestUrl, TestSupport.Ct);
        _time.Advance(TimeSpan.FromSeconds(RetryAfterSeconds));

        var response = await pending.WaitAsync(Patience, TestSupport.Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_FiveRateLimitsWithTenRetries_Succeeds()
    {
        var responses = Enumerable.Repeat<Func<HttpResponseMessage>>(() => Limited(TimeSpan.Zero), ThrottledAttempts)
            .Append(Ok)
            .ToArray();
        var (client, _, inner) = Build(Script(responses), LongRetries);

        var response = await client.GetAsync(TestUrl, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ThrottledAttempts + 1, inner.Requests.Count);
    }

    [Fact]
    public async Task SendAsync_RateLimitedBeyondTenRetries_ThrowsRateLimited()
    {
        var responses = Enumerable.Repeat<Func<HttpResponseMessage>>(() => Limited(TimeSpan.Zero), LongRetries + 1).ToArray();
        var (client, _, inner) = Build(Script(responses), LongRetries);

        var exception = await Assert.ThrowsAsync<RemoteSourceException>(() => client.GetAsync(TestUrl, TestSupport.Ct));

        Assert.Equal(RemoteFailureKind.RateLimited, exception.Kind);
        Assert.Equal(LongRetries + 1, inner.Requests.Count);
    }

    [Fact]
    public async Task SendAsync_HungInnerHandler_CancelsAtAttemptTimeoutWhileCallerIsLive()
    {
        var inner = new StubHttpHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Ok();
        });
        var (client, _, _) = BuildWith(inner, MaxRetries);
        using var caller = new CancellationTokenSource();

        var pending = client.GetAsync(TestUrl, caller.Token);
        while (inner.Requests.Count == 0)
        {
            await Task.Delay(1, TestSupport.Ct);
        }

        _time.Advance(AttemptTimeout);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(Patience, TestSupport.Ct));
        Assert.False(caller.IsCancellationRequested);
    }

    [Fact]
    public async Task SendAsync_CallerCancelsDuringGatePause_Throws()
    {
        var (client, _, inner) = Build(Script(() => Limited(TimeSpan.FromSeconds(RetryAfterSeconds)), Ok));
        using var caller = new CancellationTokenSource();

        var pending = client.GetAsync(TestUrl, caller.Token);
        Assert.False(pending.IsCompleted);
        await caller.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(Patience, TestSupport.Ct));
        Assert.Single(inner.Requests);
    }
}
