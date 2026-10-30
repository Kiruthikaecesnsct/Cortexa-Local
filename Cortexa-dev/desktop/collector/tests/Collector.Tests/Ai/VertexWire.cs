using System.Net;
using System.Text.Json;
using Collector.Application.Ports;
using Collector.Infrastructure.Ai;
using Collector.Infrastructure.Options;
using Collector.Tests.Support;

namespace Collector.Tests.Ai;

internal sealed class FakeVertexTokenSource(string token = VertexWire.AccessToken) : IVertexTokenSource
{
    public int Calls { get; private set; }

    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(token);
    }
}

internal static class VertexWire
{
    public const string AccessToken = "ya29.vertex-test-token";
    public const string ServedModel = "gemini-3.6-flash-002";
    public const string ProjectId = "cortexa-test-project";
    public const string BaseAddress = "https://europe-west4-aiplatform.googleapis.com/";

    public static readonly AiRequest Request = new()
    {
        SystemText = "system instructions",
        UserText = "user message",
        OutputSchemaJson = "{\"type\":\"object\",\"properties\":{\"items\":{\"type\":\"array\"}}}",
    };

    public static string Response(string finishReason, string text = "{\"items\":[]}", string model = ServedModel) =>
        "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":" + JsonSerializer.Serialize(text) + "}]}," +
        "\"finishReason\":\"" + finishReason + "\"}],\"modelVersion\":\"" + model + "\"}";

    public static string ErrorBody(int status) =>
        "{\"error\":{\"code\":" + status + ",\"message\":\"upstream said vx-leaky-secret\",\"status\":\"ERROR\"}}";

    public static string InputTooLargeErrorBody(int tokenCount, int limit) =>
        "{\"error\":{\"code\":400,\"message\":\"The input token count (" + tokenCount +
        ") exceeds the maximum number of tokens allowed (" + limit + ").\",\"status\":\"INVALID_ARGUMENT\"}}";

    public static VertexDirectProvider Provider(
        StubHttpHandler handler,
        VertexProviderOptions? options = null,
        IVertexTokenSource? tokenSource = null)
    {
        var settings = Microsoft.Extensions.Options.Options.Create(options ?? DefaultOptions());
        var factory = new BaseAddressClientFactory(handler, BaseAddress);
        return new VertexDirectProvider(factory, tokenSource ?? new FakeVertexTokenSource(), settings);
    }

    public static VertexProviderOptions DefaultOptions() => new()
    {
        ProjectId = ProjectId,
        Location = "europe-west4",
        Model = "gemini-3.6-flash",
        MaxRetries = 0,
    };

    public static StubHttpHandler Ok(string finishReason = "STOP") =>
        StubHttpHandler.Returning(HttpStatusCode.OK, Response(finishReason));
}
