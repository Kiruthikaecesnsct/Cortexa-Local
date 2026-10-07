using System.Net;
using System.Reflection;
using System.Text.Json;
using Anthropic;
using Anthropic.Core;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Collector.Application.Ports;
using Collector.Infrastructure.Ai;
using Collector.Infrastructure.Options;
using Collector.Tests.Support;

namespace Collector.Tests.Ai;

internal static class AnthropicWire
{
    public const string ApiKey = "sk-test-key-value";

    private const string MessageJson =
        "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"claude-test\"," +
        "\"content\":[{\"type\":\"text\",\"text\":\"{}\"}],\"stop_reason\":\"end_turn\",\"stop_sequence\":null," +
        "\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}";

    private static readonly Assembly Infrastructure = typeof(ClaudeDirectProvider).Assembly;

    public static AnthropicClient Client(StubHttpHandler handler) => new(new ClientOptions
    {
        ApiKey = ApiKey,
        BaseUrl = "http://localhost",
        HttpClient = new HttpClient(handler),
        MaxRetries = 0,
    });

    public static StubHttpHandler OkHandler() => StubHttpHandler.Returning(HttpStatusCode.OK, MessageJson);

    public static MessageCreateParams CreateParams(AiRequest request, AiProviderOptions options)
    {
        var method = Infrastructure.GetType("Collector.Infrastructure.Ai.ClaudeRequestFactory")!.GetMethod("Create")!;
        return (MessageCreateParams)method.Invoke(null, [request, options])!;
    }

    public static async Task<JsonDocument> CaptureBodyAsync(AiRequest request, AiProviderOptions options)
    {
        var handler = OkHandler();
        await Client(handler).Messages.Create(CreateParams(request, options), TestSupport.Ct);
        return JsonDocument.Parse(handler.Requests.Single().Body);
    }

    public static AiProviderException Map(AnthropicException exception)
    {
        var method = Infrastructure.GetType("Collector.Infrastructure.Ai.AnthropicErrorMapper")!.GetMethod("Map")!;
        return (AiProviderException)method.Invoke(null, [exception])!;
    }

    public static async Task<AnthropicException> ThrownForAsync(Func<CapturedRequest, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        var client = Client(new StubHttpHandler(respond));
        var request = new AiRequest { SystemText = "s", UserText = "u", OutputSchemaJson = "{\"type\":\"object\"}" };
        var parameters = CreateParams(request, new AiProviderOptions());
        return await Assert.ThrowsAnyAsync<AnthropicException>(() => client.Messages.Create(parameters, TestSupport.Ct));
    }
}
