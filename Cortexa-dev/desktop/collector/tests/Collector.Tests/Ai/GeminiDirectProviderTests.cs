using System.Net;
using Collector.Application.Ports;
using Collector.Infrastructure.Ai;
using Collector.Infrastructure.Options;
using Collector.Tests.Support;

namespace Collector.Tests.Ai;

public sealed class GeminiDirectProviderTests
{
    private const string BlockedPromptJson =
        "{\"promptFeedback\":{\"blockReason\":\"SAFETY\"},\"modelVersion\":\"gemini-served-001\"}";

    private const string ThoughtAndAnswerJson =
        "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[" +
        "{\"text\":\"private reasoning\",\"thought\":true},{\"text\":\"{\\\"items\\\":\"},{\"text\":\"[]}\"}]}," +
        "\"finishReason\":\"STOP\"}]}";

    private static Task<AiCompletion> CompleteAsync(StubHttpHandler handler, GeminiProviderOptions? options = null) =>
        GeminiWire.Provider(handler, options).CompleteAsync(GeminiWire.Request, TestSupport.Ct);

    [Theory]
    [InlineData("STOP", AiOutcome.Completed)]
    [InlineData("MAX_TOKENS", AiOutcome.Truncated)]
    [InlineData("SAFETY", AiOutcome.Refused)]
    [InlineData("RECITATION", AiOutcome.Refused)]
    [InlineData("BLOCKLIST", AiOutcome.Refused)]
    [InlineData("PROHIBITED_CONTENT", AiOutcome.Refused)]
    [InlineData("SPII", AiOutcome.Refused)]
    [InlineData("OTHER", AiOutcome.Completed)]
    public async Task CompleteAsync_FinishReason_MapsToOutcome(string finishReason, AiOutcome expected)
    {
        var completion = await CompleteAsync(GeminiWire.Ok(finishReason));

        Assert.Equal(expected, completion.Outcome);
    }

    [Fact]
    public async Task CompleteAsync_PromptBlockedWithoutCandidates_IsRefused()
    {
        var completion = await CompleteAsync(StubHttpHandler.Returning(HttpStatusCode.OK, BlockedPromptJson));

        Assert.Equal(AiOutcome.Refused, completion.Outcome);
        Assert.Equal(string.Empty, completion.Text);
    }

    [Fact]
    public async Task CompleteAsync_ThoughtParts_AreExcludedAndTextJoined()
    {
        var completion = await CompleteAsync(StubHttpHandler.Returning(HttpStatusCode.OK, ThoughtAndAnswerJson));

        Assert.Equal("{\"items\":[]}", completion.Text);
    }

    [Fact]
    public async Task CompleteAsync_ServedModelVersion_IsReported()
    {
        var completion = await CompleteAsync(GeminiWire.Ok());

        Assert.Equal(GeminiWire.ServedModel, completion.Model);
    }

    [Fact]
    public async Task CompleteAsync_NoModelVersion_FallsBackToConfiguredModel()
    {
        var completion = await CompleteAsync(
            StubHttpHandler.Returning(HttpStatusCode.OK, ThoughtAndAnswerJson),
            new GeminiProviderOptions { Model = "gemini-configured", MaxRetries = 0 });

        Assert.Equal("gemini-configured", completion.Model);
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
    public async Task CompleteAsync_TooManyRequests_ThrowsQuotaExceeded()
    {
        var exception = await ThrowsForStatusAsync(HttpStatusCode.TooManyRequests);

        Assert.Equal(AiFailureKind.QuotaExceeded, exception.Kind);
    }

    [Fact]
    public async Task CompleteAsync_ErrorBody_IsNotCopiedIntoMessage()
    {
        var exception = await ThrowsForStatusAsync(HttpStatusCode.BadRequest);

        Assert.DoesNotContain("gm-leaky-secret", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(GeminiWire.ApiKey, exception.Message, StringComparison.Ordinal);
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
            () => GeminiWire.Provider(handler).CompleteAsync(GeminiWire.Request, source.Token));
    }

    [Fact]
    public async Task CompleteAsync_MaxRetriesZero_SendsOneRequestOnServerError()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.ServiceUnavailable, GeminiWire.ErrorBody(503));

        await Assert.ThrowsAsync<AiProviderException>(() => CompleteAsync(handler));

        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CompleteAsync_NoKey_ThrowsMissingApiKeyWithoutCallingGemini(string? stored)
    {
        var secrets = new InMemorySecretStore();
        if (stored is not null)
        {
            secrets.Values[Collector.Application.Secrets.SecretSlot.GeminiApiKey] = stored;
        }

        var handler = GeminiWire.Ok();
        var provider = GeminiWire.Provider(handler, secrets: secrets);

        var exception = await Assert.ThrowsAsync<AiProviderException>(() => provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct));

        Assert.Equal(AiFailureKind.MissingApiKey, exception.Kind);
        Assert.Equal(GeminiClientFactory.MissingKeyMessage, exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CompleteAsync_OnlyClaudeKeyStored_ThrowsMissingApiKey()
    {
        var secrets = new InMemorySecretStore();
        secrets.Values[Collector.Application.Secrets.SecretSlot.AnthropicApiKey] = "sk-claude-only";

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => GeminiWire.Provider(GeminiWire.Ok(), secrets: secrets).CompleteAsync(GeminiWire.Request, TestSupport.Ct));

        Assert.Equal(AiFailureKind.MissingApiKey, exception.Kind);
    }

    private static async Task<AiProviderException> ThrowsForStatusAsync(HttpStatusCode status)
    {
        var handler = StubHttpHandler.Returning(status, GeminiWire.ErrorBody((int)status));
        return await Assert.ThrowsAsync<AiProviderException>(() => CompleteAsync(handler));
    }
}
