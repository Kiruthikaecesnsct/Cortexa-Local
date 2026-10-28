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
    GeminiModelFallback fallback,
    GeminiQuotaWait quotaWait,
    ILogger<GeminiDirectProvider> logger) : IAiProvider
{
    private const string AllModelsExhaustedMessage = "All configured Gemini models are out of quota for this run.";
    private const string AllKeysFailedMessage = "All configured Gemini keys failed.";

    public async Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
    {
        var context = await CreateContextAsync(request, cancellationToken);
        AiProviderException? lastFailure = null;
        foreach (var step in fallback.Sequence(request))
        {
            if (!fallback.IsUsable(step.Model))
            {
                continue;
            }

            var outcome = await TryStepAsync(new ModelAttempt(context, step.Model, step.IsFallback), cancellationToken);
            if (outcome.Completion is { } completion)
            {
                return completion;
            }

            lastFailure = outcome.QuotaFailure ?? lastFailure;
        }

        throw lastFailure ?? new AiProviderException(AiFailureKind.QuotaExceeded, AllModelsExhaustedMessage);
    }

    private async Task<ModelOutcome> TryStepAsync(ModelAttempt attempt, CancellationToken cancellationToken)
    {
        try
        {
            return await RunModelAsync(attempt, cancellationToken);
        }
        catch (AiProviderException exception) when (exception.Kind == AiFailureKind.Transient && exception.InnerException is not OperationCanceledException)
        {
            logger.LogWarning("Gemini model {Model} is temporarily unavailable; trying the next model.", attempt.Model);
            return new ModelOutcome(null, exception);
        }
    }

    private async Task<AttemptContext> CreateContextAsync(AiRequest request, CancellationToken cancellationToken)
    {
        var keys = await keyStore.GetKeysAsync(cancellationToken);
        if (keys.Keys.Count == 0)
        {
            throw new AiProviderException(AiFailureKind.MissingApiKey, GeminiClientFactory.MissingKeyMessage);
        }

        var activeId = userSettings.GetGeminiActiveKeyId();
        return new AttemptContext(
            request,
            GeminiRequestFactory.Create(request, options.Value),
            keys.Keys,
            GeminiKeyRotation.OrderFrom(keys.Keys, activeId),
            activeId);
    }

    private async Task<ModelOutcome> RunModelAsync(ModelAttempt attempt, CancellationToken cancellationToken)
    {
        if (attempt.IsFallback && await CountQuotaFailureAsync(attempt, cancellationToken) is { } countFailure)
        {
            return new ModelOutcome(null, countFailure);
        }

        var result = await TryModelAsync(attempt, cancellationToken);
        if (result.Completion is null && result.AllKeysQuota)
        {
            result = await RetryAfterWaitAsync(attempt, result, cancellationToken);
        }

        return Conclude(attempt.Model, result);
    }

    private async Task<AiProviderException?> CountQuotaFailureAsync(ModelAttempt attempt, CancellationToken cancellationToken)
    {
        try
        {
            await fallback.EnsureFitsAsync(attempt.Context.Request, attempt.Model, cancellationToken);
            return null;
        }
        catch (AiProviderException exception) when (exception is not AiInputTooLargeException
            && exception.Kind == AiFailureKind.QuotaExceeded)
        {
            return exception;
        }
    }

    private async Task<ModelAttemptResult> RetryAfterWaitAsync(
        ModelAttempt attempt,
        ModelAttemptResult first,
        CancellationToken cancellationToken)
    {
        if (quotaWait.DelayFor(first.LastFailure) is not { } delay)
        {
            return first;
        }

        logger.LogInformation(
            "Gemini model {Model} is rate limited; waiting {DelaySeconds}s before one retry.",
            attempt.Model,
            delay.TotalSeconds);
        await quotaWait.WaitAsync(delay, cancellationToken);
        return await TryModelAsync(attempt, cancellationToken);
    }

    private ModelOutcome Conclude(string model, ModelAttemptResult result)
    {
        if (result.Completion is { } completion)
        {
            return new ModelOutcome(completion, null);
        }

        var failure = result.LastFailure ?? new AiProviderException(AiFailureKind.Permanent, AllKeysFailedMessage);
        if (!result.AllKeysQuota)
        {
            throw failure;
        }

        fallback.MarkExhausted(model);
        logger.LogWarning("Gemini model {Model} is out of quota on every configured key; trying the next model.", model);
        return new ModelOutcome(null, failure);
    }

    private async Task<ModelAttemptResult> TryModelAsync(ModelAttempt attempt, CancellationToken cancellationToken)
    {
        AiProviderException? lastFailure = null;
        var allQuota = true;
        foreach (var candidate in attempt.Context.Order)
        {
            var result = await TryKeyAsync(attempt, candidate, cancellationToken);
            if (result.Completion is not null)
            {
                return new ModelAttemptResult(result.Completion, null, false);
            }

            lastFailure = result.Failure;
            allQuota &= result.Failure?.Kind == AiFailureKind.QuotaExceeded;
        }

        return new ModelAttemptResult(null, lastFailure, allQuota && lastFailure is not null);
    }

    private async Task<KeyAttemptResult> TryKeyAsync(ModelAttempt attempt, GeminiKey candidate, CancellationToken cancellationToken)
    {
        var context = attempt.Context;
        var client = clientFactory.GetOrCreate(context.Keys, candidate.Id);
        logger.LogInformation("Gemini request using key {KeyId} on model {Model}.", candidate.Id, attempt.Model);
        try
        {
            var response = await SendAsync(client, attempt.Model, context.Call, cancellationToken);
            LogUsage(response, candidate.Id, attempt.Model);
            keyStatusStore.SetStatus(candidate.Id, GeminiKeyStatus.Ok);
            await PersistActiveKeyAsync(candidate.Id, context.ActiveId, cancellationToken);
            return new KeyAttemptResult(GeminiResponseReader.ToCompletion(response, attempt.Model), null);
        }
        catch (AiProviderException exception) when (exception.Kind == AiFailureKind.Transient)
        {
            throw;
        }
        catch (AiProviderException exception)
        {
            keyStatusStore.SetStatus(candidate.Id, StatusFor(exception.Kind));
            logger.LogWarning(
                "Gemini key {KeyId} failed with {Kind} on model {Model}; rotating to next configured key.",
                candidate.Id,
                exception.Kind,
                attempt.Model);
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

    private void LogUsage(GenerateContentResponse response, string keyId, string model)
    {
        var usage = response.UsageMetadata;
        logger.LogDebug(
            "Gemini call on key {KeyId} and model {Model} used {PromptTokens} prompt, {OutputTokens} output and {ThoughtTokens} thinking tokens.",
            keyId,
            model,
            usage?.PromptTokenCount,
            usage?.CandidatesTokenCount,
            usage?.ThoughtsTokenCount);
    }

    private sealed record AttemptContext(
        AiRequest Request,
        GeminiRequest Call,
        IReadOnlyList<GeminiKey> Keys,
        IReadOnlyList<GeminiKey> Order,
        string? ActiveId);

    private sealed record ModelAttempt(AttemptContext Context, string Model, bool IsFallback);

    private sealed record ModelOutcome(AiCompletion? Completion, AiProviderException? QuotaFailure);

    private sealed record ModelAttemptResult(AiCompletion? Completion, AiProviderException? LastFailure, bool AllKeysQuota);

    private sealed record KeyAttemptResult(AiCompletion? Completion, AiProviderException? Failure);
}
