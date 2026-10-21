using System.Net;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Infrastructure.Ai;
using Collector.Infrastructure.Options;
using Collector.Tests.Support;

namespace Collector.Tests.Ai;

public sealed class GeminiInputBudgetTests
{
    private const string FirstKeyValue = "first-key-value";
    private const string SecondKeyValue = "second-key-value";
    private const int CountedTokens = 4321;
    private const int ConfiguredLimit = 777;
    private const string KeyHeader = "x-goog-api-key";

    private static readonly GeminiKey FirstKey = new("key-first", FirstKeyValue);
    private static readonly GeminiKey SecondKey = new("key-second", SecondKeyValue);

    private static string CountBody(int? total) => total is null ? "{}" : "{\"totalTokens\":" + total + "}";

    private static StubHttpHandler RespondByKey(IReadOnlyDictionary<string, HttpResponseMessage> responses) =>
        new((request, _) => Task.FromResult(responses.TryGetValue(request.Headers[KeyHeader], out var response)
            ? response
            : StubHttpHandler.Json(HttpStatusCode.OK, CountBody(CountedTokens))));

    private static FakeGeminiKeyStore Keys(params GeminiKey[] keys)
    {
        var store = new FakeGeminiKeyStore();
        store.Keys.AddRange(keys);
        return store;
    }

    private static GeminiInputBudget Budget(
        StubHttpHandler handler,
        FakeGeminiKeyStore keys,
        FakeUserSettingsStore? settings = null,
        GeminiProviderOptions? options = null)
    {
        var wrapped = Microsoft.Extensions.Options.Options.Create(options ?? new GeminiProviderOptions { MaxRetries = 0 });
        var factory = new GeminiClientFactory(new StubHttpClientFactory(handler), wrapped);
        return new GeminiInputBudget(factory, keys, settings ?? new FakeUserSettingsStore(), wrapped);
    }

    [Fact]
    public async Task CountAsync_Success_ReturnsTotalTokensAndSendsBothTexts()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, CountBody(CountedTokens));

        var count = await Budget(handler, Keys(FirstKey)).CountAsync(GeminiWire.Request, TestSupport.Ct);

        var captured = handler.Requests.Single();
        Assert.Equal(CountedTokens, count);
        Assert.Contains("system instructions", captured.Body, StringComparison.Ordinal);
        Assert.Contains("user message", captured.Body, StringComparison.Ordinal);
        Assert.Contains("countTokens", captured.Uri?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CountAsync_NullTotal_ThrowsPermanent()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, CountBody(null));

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => Budget(handler, Keys(FirstKey)).CountAsync(GeminiWire.Request, TestSupport.Ct));

        Assert.Equal(AiFailureKind.Permanent, exception.Kind);
        Assert.Equal("Gemini returned no token count.", exception.Message);
    }

    [Fact]
    public async Task CountAsync_NoKeys_ThrowsMissingApiKey()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, CountBody(CountedTokens));

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => Budget(handler, Keys()).CountAsync(GeminiWire.Request, TestSupport.Ct));

        Assert.Equal(AiFailureKind.MissingApiKey, exception.Kind);
        Assert.Equal(GeminiClientFactory.MissingKeyMessage, exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CountAsync_FirstKeyRejected_RotatesWithoutWritingState()
    {
        var handler = RespondByKey(new Dictionary<string, HttpResponseMessage>
        {
            [FirstKeyValue] = StubHttpHandler.Json(HttpStatusCode.Unauthorized, GeminiWire.ErrorBody(401)),
        });
        var settings = new FakeUserSettingsStore();

        var count = await Budget(handler, Keys(FirstKey, SecondKey), settings).CountAsync(GeminiWire.Request, TestSupport.Ct);

        Assert.Equal(CountedTokens, count);
        Assert.Null(settings.GeminiActiveKeyId);
        Assert.Equal(0, settings.SaveCalls);
    }

    [Fact]
    public async Task CountAsync_AllKeysQuota_ThrowsLastFailureWithStatus()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.TooManyRequests, GeminiWire.ErrorBody(429));

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => Budget(handler, Keys(FirstKey, SecondKey)).CountAsync(GeminiWire.Request, TestSupport.Ct));

        Assert.Equal(AiFailureKind.QuotaExceeded, exception.Kind);
        Assert.Equal((int)HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task CountAsync_TransientFailure_RethrowsWithoutTryingNextKey()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.ServiceUnavailable, GeminiWire.ErrorBody(503));

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => Budget(handler, Keys(FirstKey, SecondKey)).CountAsync(GeminiWire.Request, TestSupport.Ct));

        Assert.Equal(AiFailureKind.Transient, exception.Kind);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CountAsync_Timeout_MapsToTransient()
    {
        var handler = new StubHttpHandler((_, _) => throw new TaskCanceledException("timed out"));

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => Budget(handler, Keys(FirstKey)).CountAsync(GeminiWire.Request, TestSupport.Ct));

        Assert.Equal(AiFailureKind.Transient, exception.Kind);
    }

    [Fact]
    public async Task CountAsync_ActiveKeySet_UsesActiveKeyFirst()
    {
        var handler = StubHttpHandler.Returning(HttpStatusCode.OK, CountBody(CountedTokens));
        var settings = new FakeUserSettingsStore();
        await settings.SaveGeminiActiveKeyIdAsync(SecondKey.Id, TestSupport.Ct);
        var savesBefore = settings.SaveCalls;

        await Budget(handler, Keys(FirstKey, SecondKey), settings).CountAsync(GeminiWire.Request, TestSupport.Ct);

        Assert.Equal(SecondKeyValue, handler.Requests.Single().Headers[KeyHeader]);
        Assert.Equal(savesBefore, settings.SaveCalls);
    }

    [Fact]
    public void MaxInputTokens_ReflectsOptions()
    {
        var budget = Budget(
            StubHttpHandler.Returning(HttpStatusCode.OK),
            Keys(FirstKey),
            options: new GeminiProviderOptions { MaxInputTokens = ConfiguredLimit });

        Assert.Equal(ConfiguredLimit, budget.MaxInputTokens);
    }
}
