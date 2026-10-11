using System.Net;
using System.Text.Json;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Infrastructure.Ai;
using Collector.Infrastructure.Options;
using Collector.Tests.Support;

namespace Collector.Tests.Ai;

internal static class GroqWire
{
    public const string ApiKey = "gq-test-key-value";
    public const string ServedModel = "groq-served-001";
    public const string BaseAddress = "https://groq.test/openai/v1/";

    public static readonly AiRequest Request = new()
    {
        SystemText = "system instructions",
        UserText = "user message",
        OutputSchemaJson = "{\"type\":\"object\",\"properties\":{\"items\":{\"type\":\"array\"}}}",
    };

    public static string Response(string finishReason, string text = "{\"items\":[]}", string model = ServedModel) =>
        "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + JsonSerializer.Serialize(text) + "}," +
        "\"finish_reason\":\"" + finishReason + "\"}],\"model\":\"" + model + "\"," +
        "\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5,\"total_tokens\":15}}";

    public static string ErrorBody(int status) =>
        "{\"error\":{\"code\":" + status + ",\"message\":\"upstream said gq-leaky-secret\",\"type\":\"invalid_request_error\"}}";

    public static GroqDirectProvider Provider(
        StubHttpHandler handler,
        GroqProviderOptions? options = null,
        InMemorySecretStore? secrets = null)
    {
        var settings = Microsoft.Extensions.Options.Options.Create(options ?? new GroqProviderOptions { MaxRetries = 0 });
        var store = secrets ?? WithKey();
        var factory = new BaseAddressClientFactory(handler, BaseAddress);
        return new GroqDirectProvider(factory, store, settings);
    }

    public static InMemorySecretStore WithKey()
    {
        var store = new InMemorySecretStore();
        store.Values[SecretSlot.GroqApiKey] = ApiKey;
        return store;
    }

    public static StubHttpHandler Ok(string finishReason = "stop") =>
        StubHttpHandler.Returning(HttpStatusCode.OK, Response(finishReason));

    public static async Task<(JsonElement Root, CapturedRequest Request)> CaptureAsync(AiRequest request, GroqProviderOptions options)
    {
        var handler = Ok();
        await Provider(handler, options).CompleteAsync(request, TestSupport.Ct);
        var captured = handler.Requests.Single();
        using var document = JsonDocument.Parse(captured.Body);
        return (document.RootElement.Clone(), captured);
    }
}
