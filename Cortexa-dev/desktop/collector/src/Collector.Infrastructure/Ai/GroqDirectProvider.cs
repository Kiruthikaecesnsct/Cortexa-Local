using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Infrastructure.Http;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Ai;

public sealed class GroqDirectProvider(
    IHttpClientFactory httpClients,
    ISecretStore secrets,
    IOptions<GroqProviderOptions> options) : IAiProvider
{
    public const string MissingKeyMessage = "Add your Groq key in Settings.";

    private const string ChatCompletionsPath = "chat/completions";

    private const string BearerScheme = "Bearer";

    public async Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
    {
        var key = await secrets.ReadAsync(SecretSlot.GroqApiKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new AiProviderException(AiFailureKind.MissingApiKey, MissingKeyMessage);
        }

        var call = GroqRequestFactory.Create(request, options.Value);
        var body = await SendAsync(key, call, cancellationToken).ConfigureAwait(false);
        return Parse(body, call.Model);
    }

    private async Task<string> SendAsync(string key, GroqChatRequestWire call, CancellationToken cancellationToken)
    {
        var client = httpClients.CreateClient(HttpClientNames.Groq);
        using var httpRequest = BuildRequest(call, key);
        try
        {
            using var response = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw exception.StatusCode is { } status
                ? GroqErrorMapper.Map(status, exception)
                : GroqErrorMapper.MapUnreachable(exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw GroqErrorMapper.MapTimeout(exception);
        }
    }

    private static HttpRequestMessage BuildRequest(GroqChatRequestWire call, string key)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsPath)
        {
            Content = JsonContent.Create(call, GroqJson.Default.GroqChatRequestWire),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, key);
        return httpRequest;
    }

    private static AiCompletion Parse(string body, string configuredModel)
    {
        try
        {
            var response = JsonSerializer.Deserialize(body, GroqJson.Default.GroqChatResponseWire)
                ?? throw new JsonException("Groq response body was empty.");
            return GroqResponseReader.ToCompletion(response, configuredModel);
        }
        catch (JsonException exception)
        {
            throw GroqErrorMapper.MapMalformed(exception);
        }
    }
}
