using Cortexa.ModelRouter.Application.DTOs;
using Cortexa.ModelRouter.Application.Exceptions;
using Cortexa.ModelRouter.Application.Interfaces;
using Cortexa.ModelRouter.Domain.ValueObjects;
using Cortexa.ModelRouter.Infrastructure.Configuration;
using Cortexa.ModelRouter.Infrastructure.Providers.Foundry.Mapping;
using Cortexa.ModelRouter.Infrastructure.Security;
using Cortexa.ModelRouter.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Cortexa.ModelRouter.Infrastructure.Providers.Foundry;

public sealed class FoundryProvider : IModelProvider
{
    private const int ErrorBodySnippetMaxChars = 2048;

    private readonly FoundrySettings _settings;
    private readonly HttpClient _http;
    private readonly IProviderKeyResolver _keyResolver;
    private readonly IFoundryConcurrencyLimiter _concurrencyLimiter;
    private readonly ILogger<FoundryProvider> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FoundryProvider(
        IOptions<FoundrySettings> settings,
        HttpClient http,
        IProviderKeyResolver keyResolver,
        IFoundryConcurrencyLimiter concurrencyLimiter,
        ILogger<FoundryProvider> logger)
    {
        _settings = settings.Value;
        _http = http;
        _keyResolver = keyResolver;
        _concurrencyLimiter = concurrencyLimiter;
        _logger = logger;
    }

    public async Task<ModelResult> CompleteAsync(ModelRequest request, CancellationToken ct = default)
    {
        var deployment = ResolveDeployment(request);
        var options = _settings.ResolveOptions(deployment);
        using var lease = await _concurrencyLimiter.AcquireAsync(deployment, options.MaxInFlight, _settings.AcquireWaitSeconds, ct);

        var apiKey = await _keyResolver.ResolveAsync(_settings.ApiKeySecretName, ct);
        var body = BuildRequestBody(request, deployment, options, stream: false);
        using var httpRequest = BuildHttpRequest(apiKey, body);

        var callTimeout = _settings.ResolveCallTimeout(request.TaskKind);
        LogResolvedCallTimeout(request.TaskKind, deployment, callTimeout, options.MaxInFlight);
        using var callTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        callTimeoutCts.CancelAfter(TimeSpan.FromSeconds(callTimeout));

        try
        {
            using var response = await _http.SendAsync(httpRequest, callTimeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var errorDetails = await ParseAndLogUpstreamErrorAsync(response, deployment, request.TaskKind, callTimeoutCts.Token);
                throw CreateModelProviderException((int)response.StatusCode, errorDetails);
            }

            var foundryResponse = await response.Content.ReadFromJsonAsync<FoundryResponse>(JsonOptions, callTimeoutCts.Token)
                ?? throw new ModelProviderException("Foundry", null, "Foundry returned empty response");

            return MapResult(foundryResponse);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new FoundryTimeoutException(deployment, callTimeout);
        }
    }

    public async IAsyncEnumerable<ModelChunk> StreamAsync(
        ModelRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var deployment = ResolveDeployment(request);
        var options = _settings.ResolveOptions(deployment);
        using var lease = await _concurrencyLimiter.AcquireAsync(deployment, options.MaxInFlight, _settings.AcquireWaitSeconds, ct);

        var apiKey = await _keyResolver.ResolveAsync(_settings.ApiKeySecretName, ct);
        var body = BuildRequestBody(request, deployment, options, stream: true);
        using var httpRequest = BuildHttpRequest(apiKey, body);

        var callTimeout = _settings.ResolveCallTimeout(request.TaskKind);
        LogResolvedCallTimeout(request.TaskKind, deployment, callTimeout, options.MaxInFlight);
        using var callTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        callTimeoutCts.CancelAfter(TimeSpan.FromSeconds(callTimeout));

        StreamReader reader;
        try
        {
            var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, callTimeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var errorDetails = await ParseAndLogUpstreamErrorAsync(response, deployment, request.TaskKind, callTimeoutCts.Token);
                throw CreateModelProviderException((int)response.StatusCode, errorDetails);
            }

            var stream = await response.Content.ReadAsStreamAsync(callTimeoutCts.Token);
            reader = new StreamReader(stream);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new FoundryTimeoutException(deployment, callTimeout);
        }

        using (reader)
        {
            var context = new FoundryCallContext(deployment, callTimeout);
            await foreach (var chunk in ReadStreamAsync(reader, callTimeoutCts, context, ct))
                yield return chunk;
        }
    }

    private readonly record struct FoundryCallContext(string Deployment, int CallTimeoutSeconds);

    private static async IAsyncEnumerable<ModelChunk> ReadStreamAsync(
        StreamReader reader,
        CancellationTokenSource callTimeoutCts,
        FoundryCallContext context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!reader.EndOfStream)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(callTimeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new FoundryTimeoutException(context.Deployment, context.CallTimeoutSeconds);
            }

            var chunk = ParseSseLine(line);
            if (chunk is null)
                continue;

            if (chunk.Value.isFinal)
                break;

            yield return chunk.Value.chunk;
        }
    }

    private static (ModelChunk chunk, bool isFinal)? ParseSseLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ", StringComparison.Ordinal))
            return null;

        var data = line["data: ".Length..];
        if (data == "[DONE]")
            return (default!, true);

        var chunk = ParseStreamChunk(data);
        if (chunk is null)
            return null;

        return (chunk, false);
    }

    private const float SupportedTemperature = 1.0f;

    private readonly record struct ErrorDetails(
        string BodySnippet,
        string? ErrorCode,
        string? ErrorParam,
        string[]? ContentFilterCategories);

    private async Task<ErrorDetails> ParseAndLogUpstreamErrorAsync(
        HttpResponseMessage response,
        string deployment,
        string? taskKind,
        CancellationToken ct)
    {
        string bodySnippet;
        string? errorCode = null;
        string? errorParam = null;
        string[]? contentFilterCategories = null;

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            var buffer = new char[ErrorBodySnippetMaxChars];
            var charsRead = await reader.ReadBlockAsync(buffer.AsMemory(0, buffer.Length), ct);
            bodySnippet = new string(buffer, 0, charsRead);

            if (charsRead > 0)
            {
                var errorResponse = JsonSerializer.Deserialize<FoundryErrorResponse>(bodySnippet, JsonOptions);
                if (errorResponse?.Error is { } error)
                {
                    errorCode = error.Code;
                    errorParam = error.Param;
                    var isContentFilter = error.Code == "content_filter" ||
                                         error.InnerError?.Code == "ResponsibleAIPolicyViolation";

                    if (isContentFilter && error.InnerError?.ContentFilterResult is { } filterResult)
                    {
                        contentFilterCategories = filterResult
                            .Where(kvp => kvp.Value.Filtered)
                            .Select(kvp => kvp.Key)
                            .ToArray();
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            bodySnippet = "(failed to read response body)";
        }

        _logger.LogError(
            "Foundry upstream error {StatusCode} for deployment {Deployment}, task_kind {TaskKind}, param {Param}: {BodySnippet}",
            (int)response.StatusCode,
            deployment,
            taskKind ?? "(none)",
            errorParam ?? "(none)",
            bodySnippet);

        return new ErrorDetails(bodySnippet, errorCode, errorParam, contentFilterCategories);
    }

    private static ModelProviderException CreateModelProviderException(int statusCode, ErrorDetails details)
    {
        var message = details.ErrorCode == "invalid_request_error" && !string.IsNullOrWhiteSpace(details.ErrorParam)
            ? $"Foundry returned {statusCode}: invalid_request_error on param {details.ErrorParam}"
            : $"Foundry returned {statusCode}";

        var isContentFilter = details.ErrorCode == "content_filter" ||
                            details.ContentFilterCategories?.Length > 0;

        if (isContentFilter && details.ContentFilterCategories?.Length > 0)
        {
            return new ModelProviderException(
                "Foundry",
                statusCode,
                message,
                "content_filter",
                details.ContentFilterCategories);
        }

        if (details.ErrorCode is not null)
        {
            return new ModelProviderException(
                "Foundry",
                statusCode,
                message,
                details.ErrorCode,
                null);
        }

        return new ModelProviderException("Foundry", statusCode, message);
    }

    private void LogResolvedCallTimeout(string? taskKind, string deployment, int callTimeoutSeconds, int maxInFlight)
    {
        _logger.LogInformation(
            "Foundry call timeout resolved: taskKind={TaskKind} deployment={Deployment} callTimeoutSeconds={CallTimeoutSeconds} maxInFlight={MaxInFlight}",
            taskKind,
            deployment,
            callTimeoutSeconds,
            maxInFlight);
    }

    private string ResolveDeployment(ModelRequest request)
    {
        return string.IsNullOrWhiteSpace(request.Model) ? _settings.Deployment : request.Model;
    }

    private FoundryRequest BuildRequestBody(
        ModelRequest request,
        string deployment,
        FoundryDeploymentOptions options,
        bool stream)
    {
        var messages = new[] { new FoundryMessage("user", request.Prompt) };
        var taskOverride = _settings.ResolveTaskOverride(request.TaskKind);
        var temperature = ResolveTemperature(request.Options.Temperature, options);
        var reasoningEffort = ResolveReasoningEffort(options, taskOverride);
        var effectiveMaxTokens = ResolveEffectiveMaxTokens(request.Options.MaxTokens, options.OutputTokenCap, taskOverride?.OutputTokenCap);
        var usesMaxTokens = options.TokenFieldKind == FoundryTokenFieldKind.MaxTokens;

        var responseFormat = request.Options.ForceJsonOutput ? new FoundryResponseFormat("json_object") : null;

        return new FoundryRequest(
            deployment,
            messages,
            usesMaxTokens ? null : effectiveMaxTokens,
            usesMaxTokens ? effectiveMaxTokens : null,
            temperature,
            stream ? true : null,
            reasoningEffort,
            responseFormat);
    }

    private static float? ResolveTemperature(float? requested, FoundryDeploymentOptions options)
    {
        if (!options.SendTemperature)
            return null;

        return requested == SupportedTemperature ? requested : null;
    }

    private string? ResolveReasoningEffort(FoundryDeploymentOptions options, FoundryTaskOverride? taskOverride)
    {
        if (!options.SendReasoningEffort)
            return null;

        var effort = taskOverride?.ReasoningEffort ?? _settings.ReasoningEffort;
        if (string.IsNullOrWhiteSpace(effort))
            return null;

        return ReasoningEffortResolver.Resolve(effort, options.SupportedReasoningEfforts);
    }

    private static int? ResolveEffectiveMaxTokens(int? requested, int? deploymentCap, int? taskCap)
    {
        if (requested is null)
            return null;

        var effective = requested.Value;
        if (deploymentCap is int dCap)
            effective = Math.Min(effective, dCap);
        if (taskCap is int tCap)
            effective = Math.Min(effective, tCap);

        return effective;
    }

    private HttpRequestMessage BuildHttpRequest(string apiKey, FoundryRequest body)
    {
        var url = $"{_settings.Endpoint}/openai/v1/chat/completions?api-version={_settings.ApiVersion}";
        var message = new HttpRequestMessage(HttpMethod.Post, url);
        message.Headers.Add("api-key", apiKey);
        message.Content = JsonContent.Create(body, options: JsonOptions);
        return message;
    }

    private static ModelResult MapResult(FoundryResponse response)
    {
        var content = response.Choices.Count > 0
            ? response.Choices[0].Message?.Content ?? string.Empty
            : string.Empty;

        var usage = new TokenUsage(
            response.Usage.PromptTokens,
            response.Usage.CompletionTokens,
            response.Usage.TotalTokens);

        return new ModelResult("Foundry", response.Model, content, null, usage,
            response.Choices.Count > 0 ? response.Choices[0].FinishReason : null);
    }

    private static ModelChunk? ParseStreamChunk(string data)
    {
        FoundryStreamChunk? chunk;
        try
        {
            chunk = JsonSerializer.Deserialize<FoundryStreamChunk>(data, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (chunk is null || chunk.Choices.Count == 0) return null;

        var choice = chunk.Choices[0];
        var delta = choice.Delta?.Content ?? string.Empty;
        var isFinal = choice.FinishReason is not null;

        TokenUsage? usage = null;
        if (chunk.Usage is { } u)
            usage = new TokenUsage(u.PromptTokens, u.CompletionTokens, u.TotalTokens);

        return new ModelChunk(delta, isFinal, usage);
    }
}
