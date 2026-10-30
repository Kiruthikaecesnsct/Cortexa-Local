using System.Net;
using Collector.Application.Ports;
using Collector.Infrastructure.Ai;
using Collector.Infrastructure.Options;
using Collector.Tests.Support;

namespace Collector.Tests.Ai;

public sealed class VertexDirectProviderTests
{
    private const int RejectedTokenCount = 1_234_567;
    private const int TokenLimit = 32_768;

    private static Task<AiCompletion> CompleteAsync(StubHttpHandler handler, VertexProviderOptions? options = null) =>
        VertexWire.Provider(handler, options).CompleteAsync(VertexWire.Request, TestSupport.Ct);

    [Fact]
    public async Task CompleteAsync_RegionalEndpoint_TargetsConfiguredLocationAndModelWithBearerToken()
    {
        var handler = VertexWire.Ok();
        var options = VertexWire.DefaultOptions();

        var completion = await CompleteAsync(handler, options);

        var request = handler.Requests.Single();
        var expectedUrl = VertexWire.BaseAddress +
            $"v1/projects/{options.ProjectId}/locations/{options.Location}/publishers/google/models/{options.Model}:generateContent";
        Assert.Equal(expectedUrl, request.Uri?.ToString());
        Assert.True(request.Headers.TryGetValue("Authorization", out var authorization));
        Assert.StartsWith("Bearer ", authorization, StringComparison.Ordinal);
        Assert.Equal(VertexWire.ServedModel, completion.Model);
    }

    [Theory]
    [InlineData("STOP", AiOutcome.Completed)]
    [InlineData("MAX_TOKENS", AiOutcome.Truncated)]
    [InlineData("SAFETY", AiOutcome.Refused)]
    [InlineData("OTHER", AiOutcome.Completed)]
    public async Task CompleteAsync_FinishReason_MapsToOutcome(string finishReason, AiOutcome expected)
    {
        var completion = await CompleteAsync(VertexWire.Ok(finishReason));

        Assert.Equal(expected, completion.Outcome);
    }

    [Fact]
    public async Task CompleteAsync_ResponseTextIsReturned()
    {
        var completion = await CompleteAsync(VertexWire.Ok());

        Assert.Equal("{\"items\":[]}", completion.Text);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, AiFailureKind.QuotaExceeded)]
    [InlineData(HttpStatusCode.ServiceUnavailable, AiFailureKind.Transient)]
    public async Task CompleteAsync_HttpFailures_MapToExpectedKind(HttpStatusCode status, AiFailureKind expected)
    {
        var handler = StubHttpHandler.Returning(status, VertexWire.ErrorBody((int)status));

        var exception = await Assert.ThrowsAsync<AiProviderException>(() => CompleteAsync(handler));

        Assert.Equal(expected, exception.Kind);
    }

    [Fact]
    public async Task CompleteAsync_BadRequestWithoutSizeMarker_ThrowsPermanent()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.BadRequest, VertexWire.ErrorBody(400));

        var exception = await Assert.ThrowsAsync<AiProviderException>(() => CompleteAsync(handler));

        Assert.Equal(AiFailureKind.Permanent, exception.Kind);
        Assert.IsNotType<AiInputTooLargeException>(exception);
    }

    [Fact]
    public async Task CompleteAsync_ErrorBody_IsNotCopiedIntoMessage()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.BadRequest, VertexWire.ErrorBody(400));

        var exception = await Assert.ThrowsAsync<AiProviderException>(() => CompleteAsync(handler));

        Assert.DoesNotContain("vx-leaky-secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_InputOverSizeLimit_ThrowsOnceWithoutRetry()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.BadRequest,
            VertexWire.InputTooLargeErrorBody(RejectedTokenCount, TokenLimit));

        var exception = await Assert.ThrowsAsync<AiInputTooLargeException>(() => CompleteAsync(handler));

        Assert.Equal(RejectedTokenCount, exception.TokenCount);
        Assert.Equal(TokenLimit, exception.Limit);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CompleteAsync_NetworkFailure_ThrowsTransient()
    {
        var handler = new StubHttpHandler((_, _) => throw new HttpRequestException("no route"));

        var exception = await Assert.ThrowsAsync<AiProviderException>(() => CompleteAsync(handler));

        Assert.Equal(AiFailureKind.Transient, exception.Kind);
    }

    [Fact]
    public async Task CompleteAsync_HttpTimeout_ThrowsTransient()
    {
        var handler = new StubHttpHandler((_, _) => throw new TaskCanceledException("timed out"));

        var exception = await Assert.ThrowsAsync<AiProviderException>(() => CompleteAsync(handler));

        Assert.Equal(AiFailureKind.Transient, exception.Kind);
    }

    [Fact]
    public async Task CompleteAsync_MalformedResponseBody_ThrowsTransient()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, "not json");

        var exception = await Assert.ThrowsAsync<AiProviderException>(() => CompleteAsync(handler));

        Assert.Equal(AiFailureKind.Transient, exception.Kind);
    }

    [Fact]
    public async Task CompleteAsync_CallerCanceledMidCall_PropagatesCancellation()
    {
        const int CancelAfterMilliseconds = 50;
        var handler = new StubHttpHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return StubHttpHandler.Json(HttpStatusCode.OK, "{}");
        });
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestSupport.Ct);
        source.CancelAfter(CancelAfterMilliseconds);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => VertexWire.Provider(handler).CompleteAsync(VertexWire.Request, source.Token));
    }

    [Fact]
    public async Task CompleteAsync_UsesTokenFromTokenSource()
    {
        var tokenSource = new FakeVertexTokenSource();

        await VertexWire.Provider(VertexWire.Ok(), tokenSource: tokenSource)
            .CompleteAsync(VertexWire.Request, TestSupport.Ct);

        Assert.Equal(1, tokenSource.Calls);
    }

    [Fact]
    public async Task CompleteAsync_TransientFailure_RetriesUpToMaxRetriesThenSucceeds()
    {
        var attempt = 0;
        var handler = new StubHttpHandler((_, _) =>
        {
            attempt++;
            var response = attempt < 3
                ? StubHttpHandler.Json(HttpStatusCode.ServiceUnavailable, VertexWire.ErrorBody(503))
                : StubHttpHandler.Json(HttpStatusCode.OK, VertexWire.Response("STOP"));
            return Task.FromResult(response);
        });
        var options = VertexWire.DefaultOptions();
        options.MaxRetries = 2;

        var completion = await CompleteAsync(handler, options);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("{\"items\":[]}", completion.Text);
    }

    [Fact]
    public async Task CompleteAsync_TransientFailure_ExhaustsMaxRetriesThenThrows()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.ServiceUnavailable, VertexWire.ErrorBody(503));
        var options = VertexWire.DefaultOptions();
        options.MaxRetries = 2;

        var exception = await Assert.ThrowsAsync<AiProviderException>(() => CompleteAsync(handler, options));

        Assert.Equal(AiFailureKind.Transient, exception.Kind);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task CompleteAsync_InputOverSizeLimit_NeverRetriesEvenWhenRetriesAreConfigured()
    {
        var handler = StubHttpHandler.Returning(
            HttpStatusCode.BadRequest,
            VertexWire.InputTooLargeErrorBody(RejectedTokenCount, TokenLimit));
        var options = VertexWire.DefaultOptions();
        options.MaxRetries = 3;

        await Assert.ThrowsAsync<AiInputTooLargeException>(() => CompleteAsync(handler, options));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CompleteAsync_PermanentFailure_NeverRetriesEvenWhenRetriesAreConfigured()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.NotFound, VertexWire.ErrorBody(404));
        var options = VertexWire.DefaultOptions();
        options.MaxRetries = 3;

        var exception = await Assert.ThrowsAsync<AiProviderException>(() => CompleteAsync(handler, options));

        Assert.Equal(AiFailureKind.Permanent, exception.Kind);
        Assert.Single(handler.Requests);
    }
}
