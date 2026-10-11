using System.Net;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Infrastructure.Ai;
using Collector.Infrastructure.Options;
using Collector.Tests.Support;

namespace Collector.Tests.Ai;

public sealed class GroqDirectProviderTests
{
    private static Task<AiCompletion> CompleteAsync(StubHttpHandler handler, GroqProviderOptions? options = null) =>
        GroqWire.Provider(handler, options).CompleteAsync(GroqWire.Request, TestSupport.Ct);

    [Theory]
    [InlineData("stop", AiOutcome.Completed)]
    [InlineData("length", AiOutcome.Truncated)]
    [InlineData("content_filter", AiOutcome.Refused)]
    [InlineData("tool_calls", AiOutcome.Completed)]
    public async Task CompleteAsync_FinishReason_MapsToOutcome(string finishReason, AiOutcome expected)
    {
        var completion = await CompleteAsync(GroqWire.Ok(finishReason));

        Assert.Equal(expected, completion.Outcome);
    }

    [Fact]
    public async Task CompleteAsync_ServedModel_IsReported()
    {
        var completion = await CompleteAsync(GroqWire.Ok());

        Assert.Equal(GroqWire.ServedModel, completion.Model);
    }

    [Fact]
    public async Task CompleteAsync_ResponseTextIsReturned()
    {
        var completion = await CompleteAsync(GroqWire.Ok());

        Assert.Equal("{\"items\":[]}", completion.Text);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task CompleteAsync_PermanentStatus_ThrowsPermanent(HttpStatusCode status)
    {
        var exception = await ThrowsForStatusAsync(status);

        Assert.Equal(AiFailureKind.Permanent, exception.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task CompleteAsync_RetryableStatus_ThrowsTransient(HttpStatusCode status)
    {
        var exception = await ThrowsForStatusAsync(status);

        Assert.Equal(AiFailureKind.Transient, exception.Kind);
    }

    [Fact]
    public async Task CompleteAsync_TooManyRequests_ThrowsQuotaExceededDistinctFromPermanent()
    {
        var quota = await ThrowsForStatusAsync(HttpStatusCode.TooManyRequests);
        var permanent = await ThrowsForStatusAsync(HttpStatusCode.Unauthorized);

        Assert.Equal(AiFailureKind.QuotaExceeded, quota.Kind);
        Assert.Equal(AiFailureKind.Permanent, permanent.Kind);
        Assert.NotEqual(quota.Kind, permanent.Kind);
    }

    [Fact]
    public async Task CompleteAsync_ErrorBody_IsNotCopiedIntoMessage()
    {
        var exception = await ThrowsForStatusAsync(HttpStatusCode.BadRequest);

        Assert.DoesNotContain("gq-leaky-secret", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(GroqWire.ApiKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_NetworkFailure_ThrowsTransient()
    {
        var handler = new StubHttpHandler((_, _) => throw new HttpRequestException("no route"));

        var exception = await Assert.ThrowsAsync<AiProviderException>(() => CompleteAsync(handler));

        Assert.Equal(AiFailureKind.Transient, exception.Kind);
    }

    [Fact]
    public async Task CompleteAsync_HttpRequestExceptionCarriesForbiddenStatus_ThrowsPermanent()
    {
        var handler = new StubHttpHandler(
            (_, _) => throw new HttpRequestException("key rejected", null, HttpStatusCode.Forbidden));

        var exception = await Assert.ThrowsAsync<AiProviderException>(() => CompleteAsync(handler));

        Assert.Equal(AiFailureKind.Permanent, exception.Kind);
    }

    [Fact]
    public async Task CompleteAsync_HttpRequestExceptionCarriesTooManyRequestsStatus_ThrowsQuotaExceeded()
    {
        var handler = new StubHttpHandler(
            (_, _) => throw new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests));

        var exception = await Assert.ThrowsAsync<AiProviderException>(() => CompleteAsync(handler));

        Assert.Equal(AiFailureKind.QuotaExceeded, exception.Kind);
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
            () => GroqWire.Provider(handler).CompleteAsync(GroqWire.Request, source.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CompleteAsync_NoKey_ThrowsMissingApiKeyWithoutCallingGroq(string? stored)
    {
        var secrets = new InMemorySecretStore();
        if (stored is not null)
        {
            secrets.Values[SecretSlot.GroqApiKey] = stored;
        }

        var handler = GroqWire.Ok();
        var provider = GroqWire.Provider(handler, secrets: secrets);

        var exception = await Assert.ThrowsAsync<AiProviderException>(() => provider.CompleteAsync(GroqWire.Request, TestSupport.Ct));

        Assert.Equal(AiFailureKind.MissingApiKey, exception.Kind);
        Assert.Equal(GroqDirectProvider.MissingKeyMessage, exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CompleteAsync_OnlyClaudeKeyStored_ThrowsMissingApiKey()
    {
        var secrets = new InMemorySecretStore();
        secrets.Values[SecretSlot.AnthropicApiKey] = "sk-claude-only";

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => GroqWire.Provider(GroqWire.Ok(), secrets: secrets).CompleteAsync(GroqWire.Request, TestSupport.Ct));

        Assert.Equal(AiFailureKind.MissingApiKey, exception.Kind);
    }

    private static async Task<AiProviderException> ThrowsForStatusAsync(HttpStatusCode status)
    {
        var handler = StubHttpHandler.Returning(status, GroqWire.ErrorBody((int)status));
        return await Assert.ThrowsAsync<AiProviderException>(() => CompleteAsync(handler));
    }
}
