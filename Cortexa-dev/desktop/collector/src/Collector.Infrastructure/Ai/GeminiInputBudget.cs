using Collector.Application.Ai;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Infrastructure.Options;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Ai;

public sealed class GeminiInputBudget(
    GeminiClientFactory clientFactory,
    IGeminiKeyStore keyStore,
    IUserSettingsStore userSettings,
    IOptions<GeminiProviderOptions> options) : IGeminiInputBudget
{
    private const string NoCountMessage = "Gemini returned no token count.";

    public int MaxInputTokens => options.Value.MaxInputTokens;

    public async Task<int> CountAsync(AiRequest request, CancellationToken cancellationToken)
    {
        var keys = await keyStore.GetKeysAsync(cancellationToken);
        if (keys.Keys.Count == 0)
        {
            throw new AiProviderException(AiFailureKind.MissingApiKey, GeminiClientFactory.MissingKeyMessage);
        }

        var order = GeminiKeyRotation.OrderFrom(keys.Keys, userSettings.GetGeminiActiveKeyId());
        return await CountAcrossKeysAsync(keys.Keys, order, request, cancellationToken);
    }

    private async Task<int> CountAcrossKeysAsync(
        IReadOnlyList<GeminiKey> keys,
        IReadOnlyList<GeminiKey> order,
        AiRequest request,
        CancellationToken cancellationToken)
    {
        var model = string.IsNullOrWhiteSpace(request.Model) ? options.Value.Model : request.Model;
        var contents = BuildContents(request);
        AiProviderException? lastFailure = null;
        foreach (var candidate in order)
        {
            var client = clientFactory.GetOrCreate(keys, candidate.Id);
            try
            {
                return await CountWithAsync(client, model, contents, cancellationToken);
            }
            catch (AiProviderException exception) when (exception.Kind != AiFailureKind.Transient)
            {
                lastFailure = exception;
            }
        }

        throw lastFailure ?? new AiProviderException(AiFailureKind.Permanent, "All configured Gemini keys failed.");
    }

    private static List<Content> BuildContents(AiRequest request) =>
    [
        new Content { Role = GeminiRequestFactory.UserRole, Parts = [new Part { Text = request.SystemText }] },
        new Content { Role = GeminiRequestFactory.UserRole, Parts = [new Part { Text = request.UserText }] },
    ];

    private static async Task<int> CountWithAsync(
        Client client,
        string model,
        List<Content> contents,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.Models.CountTokensAsync(model, contents, null, cancellationToken);
            return response.TotalTokens
                ?? throw new AiProviderException(AiFailureKind.Permanent, NoCountMessage);
        }
        catch (ApiException exception)
        {
            throw GeminiErrorMapper.Map(exception);
        }
        catch (HttpRequestException exception)
        {
            throw GeminiErrorMapper.MapUnreachable(exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw GeminiErrorMapper.MapTimeout(exception);
        }
    }
}
