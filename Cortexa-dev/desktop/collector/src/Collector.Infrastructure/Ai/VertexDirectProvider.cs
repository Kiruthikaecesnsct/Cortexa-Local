using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Collector.Application.Ports;
using Collector.Application.Settings;
using Collector.Infrastructure.Http;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Ai;

public sealed class VertexDirectProvider(
    IHttpClientFactory httpClients,
    IVertexTokenSource tokenSource,
    IVertexSettingsState settingsState,
    IOptions<VertexProviderOptions> options) : IAiProvider
{
    private const string BearerScheme = "Bearer";

    public async Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var effective = settingsState.GetEffectiveSettings();
        var model = string.IsNullOrWhiteSpace(request.Model) ? settings.Model : request.Model;
        var call = VertexRequestFactory.Create(request, settings);
        var token = await tokenSource.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var context = new VertexCallContext(settings, effective, model, token, call);
        var body = await SendAsync(context, cancellationToken).ConfigureAwait(false);
        return Parse(body, model);
    }

    private async Task<string> SendAsync(VertexCallContext context, CancellationToken cancellationToken)
    {
        var client = httpClients.CreateClient(HttpClientNames.Vertex);
        var totalAttempts = context.Settings.MaxRetries + 1;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SendOnceAsync(client, context, cancellationToken).ConfigureAwait(false);
            }
            catch (AiProviderException exception) when (exception.Kind == AiFailureKind.Transient && attempt < totalAttempts)
            {
            }
        }
    }

    private static async Task<string> SendOnceAsync(
        HttpClient client,
        VertexCallContext context,
        CancellationToken cancellationToken)
    {
        using var httpRequest = BuildRequest(context);
        var response = await SendRequestAsync(client, httpRequest, cancellationToken).ConfigureAwait(false);
        using (response)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw VertexErrorMapper.Map(response.StatusCode, responseBody, context.Model);
            }

            return responseBody;
        }
    }

    private static async Task<HttpResponseMessage> SendRequestAsync(
        HttpClient client,
        HttpRequestMessage httpRequest,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw exception.StatusCode is { } status
                ? VertexErrorMapper.Map(status, null, string.Empty)
                : VertexErrorMapper.MapUnreachable(exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw VertexErrorMapper.MapTimeout(exception);
        }
    }

    private static HttpRequestMessage BuildRequest(VertexCallContext context)
    {
        var uri = VertexEndpoints.GenerateContentUri(context.Effective.ProjectId, context.Effective.Location, context.Model);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(context.Call, VertexJson.Default.VertexGenerateContentRequestWire),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, context.Token);
        return httpRequest;
    }

    private sealed record VertexCallContext(
        VertexProviderOptions Settings,
        VertexEffectiveSettings Effective,
        string Model,
        string Token,
        VertexGenerateContentRequestWire Call);

    private static AiCompletion Parse(string body, string configuredModel)
    {
        try
        {
            var response = JsonSerializer.Deserialize(body, VertexJson.Default.VertexGenerateContentResponseWire)
                ?? throw new JsonException("Vertex response body was empty.");
            return VertexResponseReader.ToCompletion(response, configuredModel);
        }
        catch (JsonException exception)
        {
            throw VertexErrorMapper.MapMalformed(exception);
        }
    }
}
