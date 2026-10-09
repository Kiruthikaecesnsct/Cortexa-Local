using System.Text.Json;
using Collector.Application.Ai;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Infrastructure.Options;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Ai;

public sealed class GeminiDirectProvider(
    GeminiClientFactory clientFactory,
    IGeminiKeyStore keyStore,
    IGeminiKeyStatusStore keyStatusStore,
    IUserSettingsStore userSettings,
    IOptions<GeminiProviderOptions> options,
    ILogger<GeminiDirectProvider> logger) : IAiProvider
{
    public async Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
    {
        var keys = await keyStore.GetKeysAsync(cancellationToken);
        if (keys.Keys.Count == 0)
        {
            throw new AiProviderException(AiFailureKind.MissingApiKey, GeminiClientFactory.MissingKeyMessage);
        }

        var settings = options.Value;
        var model = string.IsNullOrWhiteSpace(request.Model) ? settings.Model : request.Model;
        var call = GeminiRequestFactory.Create(request, settings);
        var activeId = userSettings.GetGeminiActiveKeyId();
        var order = GeminiKeyRotation.OrderFrom(keys.Keys, activeId);

        AiProviderException? lastFailure = null;
        foreach (var candidate in order)
        {
            var attempt = await TryKeyAsync(candidate, keys.Keys, activeId, model, call, cancellationToken);
            if (attempt.Completion is { } completion)
            {
                return completion;
            }

            lastFailure = attempt.Failure;
        }

        throw lastFailure ?? new AiProviderException(AiFailureKind.Permanent, "All configured Gemini keys failed.");
    }

    private async Task<KeyAttemptResult> TryKeyAsync(
        GeminiKey candidate,
        IReadOnlyList<GeminiKey> keys,
        string? previousActiveId,
        string model,
        GeminiRequest call,
        CancellationToken cancellationToken)
    {
        var client = clientFactory.GetOrCreate(keys, candidate.Id);
        try
        {
            var response = await SendAsync(client, model, call, cancellationToken);
            LogUsage(response, candidate.Id);
            keyStatusStore.SetStatus(candidate.Id, GeminiKeyStatus.Ok);
            await PersistActiveKeyAsync(candidate.Id, previousActiveId, cancellationToken);
            return new KeyAttemptResult(GeminiResponseReader.ToCompletion(response, model), null);
        }
        catch (AiProviderException exception) when (exception.Kind == AiFailureKind.Transient)
        {
            throw;
        }
        catch (AiProviderException exception)
        {
            keyStatusStore.SetStatus(candidate.Id, StatusFor(exception.Kind));
            logger.LogWarning(
                "Gemini key {KeyId} failed with {Kind}; rotating to next configured key.",
                candidate.Id,
                exception.Kind);
            return new KeyAttemptResult(null, exception);
        }
    }

    private Task PersistActiveKeyAsync(string keyId, string? previousActiveId, CancellationToken cancellationToken) =>
        string.Equals(keyId, previousActiveId, StringComparison.Ordinal)
            ? Task.CompletedTask
            : userSettings.SaveGeminiActiveKeyIdAsync(keyId, cancellationToken);

    private static GeminiKeyStatus StatusFor(AiFailureKind kind) => kind switch
    {
        AiFailureKind.QuotaExceeded => GeminiKeyStatus.RateLimited,
        _ => GeminiKeyStatus.Rejected,
    };

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

    private void LogUsage(GenerateContentResponse response, string keyId)
    {
        var usage = response.UsageMetadata;
        logger.LogDebug(
            "Gemini call on key {KeyId} used {PromptTokens} prompt, {OutputTokens} output and {ThoughtTokens} thinking tokens.",
            keyId,
            usage?.PromptTokenCount,
            usage?.CandidatesTokenCount,
            usage?.ThoughtsTokenCount);
    }

    private sealed record KeyAttemptResult(AiCompletion? Completion, AiProviderException? Failure);
}
