using System.Text.Json;
using Collector.Application.Ports;
using Collector.Infrastructure.Options;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Ai;

public sealed class GeminiDirectProvider(
    GeminiClientFactory clientFactory,
    IOptions<GeminiProviderOptions> options,
    ILogger<GeminiDirectProvider> logger) : IAiProvider
{
    public async Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
    {
        var client = await clientFactory.CreateAsync(cancellationToken);
        var settings = options.Value;
        var model = string.IsNullOrWhiteSpace(request.Model) ? settings.Model : request.Model;
        var call = GeminiRequestFactory.Create(request, settings);
        var response = await SendAsync(client, model, call, cancellationToken);
        LogUsage(response);
        return GeminiResponseReader.ToCompletion(response, model);
    }

    private static async Task<GenerateContentResponse> SendAsync(
        Client client,
        string model,
        GeminiRequest call,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.Models.GenerateContentAsync(model, call.Contents, call.Config, cancellationToken);
        }
        catch (ApiException exception)
        {
            throw GeminiErrorMapper.Map(exception);
        }
        catch (HttpRequestException exception)
        {
            throw GeminiErrorMapper.MapUnreachable(exception);
        }
        catch (JsonException exception)
        {
            throw GeminiErrorMapper.MapMalformed(exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw GeminiErrorMapper.MapTimeout(exception);
        }
    }

    private void LogUsage(GenerateContentResponse response)
    {
        var usage = response.UsageMetadata;
        logger.LogDebug(
            "Gemini call used {PromptTokens} prompt, {OutputTokens} output and {ThoughtTokens} thinking tokens.",
            usage?.PromptTokenCount,
            usage?.CandidatesTokenCount,
            usage?.ThoughtsTokenCount);
    }
}
