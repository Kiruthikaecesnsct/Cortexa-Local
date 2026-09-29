using Cortexa.ModelRouter.Application.DTOs;
using Cortexa.ModelRouter.Application.Exceptions;
using Cortexa.ModelRouter.Application.Interfaces;
using Cortexa.ModelRouter.Domain.ValueObjects;
using Cortexa.ModelRouter.Infrastructure.Configuration;
using Cortexa.ModelRouter.Infrastructure.Providers.Gemini.Mapping;
using Cortexa.ModelRouter.Infrastructure.Security;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Cortexa.ModelRouter.Infrastructure.Providers.Gemini;

public sealed class GeminiProvider : IModelProvider
{
    private readonly GeminiSettings _settings;
    private readonly HttpClient _http;
    private readonly IProviderKeyResolver _keyResolver;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GeminiProvider(
        IOptions<GeminiSettings> settings,
        HttpClient http,
        IProviderKeyResolver keyResolver)
    {
        _settings = settings.Value;
        _http = http;
        _keyResolver = keyResolver;
    }

    public async Task<ModelResult> CompleteAsync(ModelRequest request, CancellationToken ct = default)
    {
        var model = ResolveModel(request);
        var apiKey = await _keyResolver.ResolveAsync(_settings.ApiKeySecretName, ct);
        var body = BuildRequestBody(request);
        using var httpRequest = BuildHttpRequest(apiKey, model, body, stream: false);
        using var response = await _http.SendAsync(httpRequest, ct);

        if (!response.IsSuccessStatusCode)
            throw await CreateModelProviderExceptionAsync(response, ct);

        var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiResponse>(JsonOptions, ct)
            ?? throw new ModelProviderException("Gemini", null, "Gemini returned empty response");

        return MapResult(geminiResponse, model);
    }

    public async IAsyncEnumerable<ModelChunk> StreamAsync(
        ModelRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = ResolveModel(request);
        var apiKey = await _keyResolver.ResolveAsync(_settings.ApiKeySecretName, ct);
        var body = BuildRequestBody(request);
        using var httpRequest = BuildHttpRequest(apiKey, model, body, stream: true);
        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
            throw await CreateModelProviderExceptionAsync(response, ct);

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var promptTokens = 0;

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var data = line["data: ".Length..];
            var (chunk, newPromptTokens) = ParseStreamChunk(data, promptTokens);
            promptTokens = newPromptTokens;

            if (chunk is not null)
                yield return chunk;
        }
    }

    private string ResolveModel(ModelRequest request) =>
        string.IsNullOrWhiteSpace(request.Model) ? _settings.Model : request.Model;

    private GeminiRequest BuildRequestBody(ModelRequest request)
    {
        var contents = new[] { new GeminiContent("user", new[] { new GeminiPart(request.Prompt) }) };
        var responseMimeType = request.Options.ForceJsonOutput ? "application/json" : null;

        var hasGenerationConfig = request.Options.MaxTokens is not null
            || request.Options.Temperature is not null
            || responseMimeType is not null;

        var generationConfig = hasGenerationConfig
            ? new GeminiGenerationConfig(request.Options.MaxTokens, request.Options.Temperature, responseMimeType)
            : null;

        return new GeminiRequest(contents, generationConfig);
    }

    internal HttpRequestMessage BuildHttpRequest(string apiKey, string model, GeminiRequest body, bool stream)
    {
        var method = stream ? "streamGenerateContent" : "generateContent";
        var url = $"{_settings.BaseUrl}/{_settings.ApiVersion}/models/{model}:{method}";
        if (stream)
            url += "?alt=sse";

        var message = new HttpRequestMessage(HttpMethod.Post, url);
        message.Headers.Add("x-goog-api-key", apiKey);
        message.Content = JsonContent.Create(body, options: JsonOptions);
        return message;
    }

    private async Task<ModelProviderException> CreateModelProviderExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? message = null;
        try
        {
            var errorBody = await response.Content.ReadFromJsonAsync<GeminiErrorResponse>(JsonOptions, ct);
            message = errorBody?.Error?.Message;
        }
        catch (JsonException)
        {
            // fall through to generic message below
        }

        var statusCode = (int)response.StatusCode;
        var description = string.IsNullOrWhiteSpace(message)
            ? $"Gemini returned {statusCode}"
            : $"Gemini returned {statusCode}: {message}";

        return new ModelProviderException("Gemini", statusCode, description);
    }

    private static ModelResult MapResult(GeminiResponse response, string model)
    {
        var candidate = response.Candidates?.FirstOrDefault();
        var content = candidate?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;

        var usage = response.UsageMetadata is { } u
            ? new TokenUsage(u.PromptTokenCount, u.CandidatesTokenCount, u.TotalTokenCount)
            : new TokenUsage(0, 0, 0);

        return new ModelResult("Gemini", response.ModelVersion ?? model, content, null, usage, candidate?.FinishReason);
    }

    private static (ModelChunk? Chunk, int PromptTokens) ParseStreamChunk(string data, int promptTokens)
    {
        GeminiResponse? chunk;
        try
        {
            chunk = JsonSerializer.Deserialize<GeminiResponse>(data, JsonOptions);
        }
        catch (JsonException)
        {
            return (null, promptTokens);
        }

        if (chunk is null)
            return (null, promptTokens);

        var candidate = chunk.Candidates?.FirstOrDefault();
        var delta = candidate?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
        var isFinal = candidate?.FinishReason is not null;

        var effectivePromptTokens = chunk.UsageMetadata?.PromptTokenCount ?? promptTokens;
        TokenUsage? usage = chunk.UsageMetadata is { } u
            ? new TokenUsage(effectivePromptTokens, u.CandidatesTokenCount, u.TotalTokenCount)
            : null;

        return (new ModelChunk(delta, isFinal, usage), effectivePromptTokens);
    }
}
