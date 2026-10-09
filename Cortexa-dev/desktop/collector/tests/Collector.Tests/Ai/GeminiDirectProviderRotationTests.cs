using System.Net;
using Collector.Application.Ai;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Infrastructure.Ai;
using Collector.Tests.Support;

namespace Collector.Tests.Ai;

public sealed class GeminiDirectProviderRotationTests
{
    private const string FirstKeyValue = "first-key-value";
    private const string SecondKeyValue = "second-key-value";
    private const string ThirdKeyValue = "third-key-value";

    private static readonly GeminiKey FirstKey = new("key-first", FirstKeyValue);
    private static readonly GeminiKey SecondKey = new("key-second", SecondKeyValue);
    private static readonly GeminiKey ThirdKey = new("key-third", ThirdKeyValue);

    private static FakeGeminiKeyStore TwoKeyStore()
    {
        var store = new FakeGeminiKeyStore();
        store.Keys.Add(FirstKey);
        store.Keys.Add(SecondKey);
        return store;
    }

    private static StubHttpHandler RespondByKey(IReadOnlyDictionary<string, HttpResponseMessage> responses) =>
        new((request, _) =>
        {
            var key = request.Headers["x-goog-api-key"];
            return Task.FromResult(responses.TryGetValue(key, out var response)
                ? response
                : StubHttpHandler.Json(HttpStatusCode.OK, GeminiWire.Response("STOP")));
        });

    [Fact]
    public async Task CompleteAsync_FirstKeyRejected_RotatesToSecondKeyAndPersistsItAsActive()
    {
        var handler = RespondByKey(new Dictionary<string, HttpResponseMessage>
        {
            [FirstKeyValue] = StubHttpHandler.Json(HttpStatusCode.Unauthorized, GeminiWire.ErrorBody(401)),
        });
        var userSettings = new FakeUserSettingsStore();
        var provider = GeminiWire.Provider(handler, keyStore: TwoKeyStore(), userSettings: userSettings);

        var completion = await provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct);

        Assert.Equal(AiOutcome.Completed, completion.Outcome);
        Assert.Equal(SecondKey.Id, userSettings.GeminiActiveKeyId);
    }

    [Fact]
    public async Task CompleteAsync_FirstKeyQuotaExceeded_RotatesToSecondKeyAndPersistsItAsActive()
    {
        var handler = RespondByKey(new Dictionary<string, HttpResponseMessage>
        {
            [FirstKeyValue] = StubHttpHandler.Json(HttpStatusCode.TooManyRequests, GeminiWire.ErrorBody(429)),
        });
        var userSettings = new FakeUserSettingsStore();
        var provider = GeminiWire.Provider(handler, keyStore: TwoKeyStore(), userSettings: userSettings);

        var completion = await provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct);

        Assert.Equal(AiOutcome.Completed, completion.Outcome);
        Assert.Equal(SecondKey.Id, userSettings.GeminiActiveKeyId);
    }

    [Fact]
    public async Task CompleteAsync_FirstKeyTransientFailure_DoesNotRotateAndPropagatesTransient()
    {
        var handler = new StubHttpHandler((_, _) => throw new HttpRequestException("no route"));
        var statusStore = new FakeGeminiKeyStatusStore();
        var userSettings = new FakeUserSettingsStore();
        var provider = GeminiWire.Provider(handler, keyStore: TwoKeyStore(), statusStore: statusStore, userSettings: userSettings);

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct));

        Assert.Equal(AiFailureKind.Transient, exception.Kind);
        Assert.Single(handler.Requests);
        Assert.Equal(GeminiKeyStatus.Untested, statusStore.GetStatus(FirstKey.Id));
        Assert.Equal(GeminiKeyStatus.Untested, statusStore.GetStatus(SecondKey.Id));
        Assert.Null(userSettings.GeminiActiveKeyId);
    }

    [Fact]
    public async Task CompleteAsync_ActiveKeyIsSecondOfThree_StartsRotationAtTheResumedKey()
    {
        var store = new FakeGeminiKeyStore();
        store.Keys.Add(FirstKey);
        store.Keys.Add(SecondKey);
        store.Keys.Add(ThirdKey);
        var userSettings = new FakeUserSettingsStore();
        await userSettings.SaveGeminiActiveKeyIdAsync(SecondKey.Id, TestSupport.Ct);
        var handler = GeminiWire.Ok();
        var provider = GeminiWire.Provider(handler, keyStore: store, userSettings: userSettings);

        await provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct);

        var firstAttemptKey = Assert.Single(handler.Requests).Headers["x-goog-api-key"];
        Assert.Equal(SecondKeyValue, firstAttemptKey);
    }

    [Fact]
    public async Task CompleteAsync_RotationEvent_LogsKeyIdsButNeverTheRawKeyValues()
    {
        var handler = RespondByKey(new Dictionary<string, HttpResponseMessage>
        {
            [FirstKeyValue] = StubHttpHandler.Json(HttpStatusCode.Unauthorized, GeminiWire.ErrorBody(401)),
        });
        var logger = new CapturingLogger<GeminiDirectProvider>();
        var provider = GeminiWire.Provider(handler, keyStore: TwoKeyStore(), logger: logger);

        await provider.CompleteAsync(GeminiWire.Request, TestSupport.Ct);

        Assert.NotEmpty(logger.Entries);
        Assert.Contains(logger.Entries, entry => entry.Contains(FirstKey.Id, StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Contains(SecondKey.Id, StringComparison.Ordinal));
        Assert.All(logger.Entries, entry =>
        {
            Assert.DoesNotContain(FirstKeyValue, entry, StringComparison.Ordinal);
            Assert.DoesNotContain(SecondKeyValue, entry, StringComparison.Ordinal);
        });
    }
}
