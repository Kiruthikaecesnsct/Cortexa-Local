using System.Net;
using System.Text.Json;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Infrastructure.Ai;
using Collector.Infrastructure.Options;
using Collector.Infrastructure.Secrets;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Tests.Ai;

internal static class GeminiWire
{
    public const string ApiKey = "gm-test-key-value";
    public const string ServedModel = "gemini-served-001";

    public static readonly AiRequest Request = new()
    {
        SystemText = "system instructions",
        UserText = "user message",
        OutputSchemaJson = "{\"type\":\"object\",\"properties\":{\"items\":{\"type\":\"array\"}}}",
    };

    public static string Response(string finishReason, string text = "{\"items\":[]}", string model = ServedModel) =>
        "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":" + JsonSerializer.Serialize(text) + "}]}," +
        "\"finishReason\":\"" + finishReason + "\"}],\"modelVersion\":\"" + model + "\"," +
        "\"usageMetadata\":{\"promptTokenCount\":10,\"candidatesTokenCount\":5,\"thoughtsTokenCount\":3}}";

    public static string ErrorBody(int status) =>
        "{\"error\":{\"code\":" + status + ",\"message\":\"upstream said gm-leaky-secret\",\"status\":\"FAILED\"}}";

    public static GeminiDirectProvider Provider(
        StubHttpHandler handler,
        GeminiProviderOptions? options = null,
        InMemorySecretStore? secrets = null,
        FakeUserSettingsStore? userSettings = null,
        IGeminiKeyStore? keyStore = null,
        FakeGeminiKeyStatusStore? statusStore = null,
        Microsoft.Extensions.Logging.ILogger<GeminiDirectProvider>? logger = null)
    {
        var settings = Microsoft.Extensions.Options.Options.Create(options ?? new GeminiProviderOptions { MaxRetries = 0 });
        var store = secrets ?? WithKey();
        var factory = new GeminiClientFactory(new StubHttpClientFactory(handler), settings);
        var effectiveKeyStore = keyStore ?? new GeminiKeyStore(store);
        var effectiveStatusStore = statusStore ?? new FakeGeminiKeyStatusStore();
        var settingsStore = userSettings ?? new FakeUserSettingsStore();
        return new GeminiDirectProvider(
            factory,
            effectiveKeyStore,
            effectiveStatusStore,
            settingsStore,
            settings,
            logger ?? NullLogger<GeminiDirectProvider>.Instance);
    }

    public static InMemorySecretStore WithKey()
    {
        var store = new InMemorySecretStore();
        store.Values[SecretSlot.GeminiApiKey] = ApiKey;
        return store;
    }

    public static StubHttpHandler Ok(string finishReason = "STOP") =>
        StubHttpHandler.Returning(HttpStatusCode.OK, Response(finishReason));

    public static async Task<(JsonElement Root, CapturedRequest Request)> CaptureAsync(AiRequest request, GeminiProviderOptions options)
    {
        var handler = Ok();
        await Provider(handler, options).CompleteAsync(request, TestSupport.Ct);
        var captured = handler.Requests.Single();
        using var document = JsonDocument.Parse(captured.Body);
        return (document.RootElement.Clone(), captured);
    }
}
