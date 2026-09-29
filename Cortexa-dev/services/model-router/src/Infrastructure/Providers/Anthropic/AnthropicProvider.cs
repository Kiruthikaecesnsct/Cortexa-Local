using Cortexa.ModelRouter.Application.DTOs;
using Cortexa.ModelRouter.Application.Exceptions;
using Cortexa.ModelRouter.Application.Interfaces;
using Cortexa.ModelRouter.Domain.ValueObjects;
using Cortexa.ModelRouter.Infrastructure.Configuration;
using Cortexa.ModelRouter.Infrastructure.Providers.Anthropic.Mapping;
using Cortexa.ModelRouter.Infrastructure.Security;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Cortexa.ModelRouter.Infrastructure.Providers.Anthropic;

public sealed class AnthropicProvider : IModelProvider
{
    private readonly AnthropicSettings _settings;
    private readonly HttpClient _http;
    private readonly IProviderKeyResolver _keyResolver;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AnthropicProvider(
        IOptions<AnthropicSettings> settings,
        HttpClient http,
        IProviderKeyResolver keyResolver)
    {
        _settings = settings.Value;
        _http = http;
        _keyResolver = keyResolver;
    }

    public async Task<ModelResult> CompleteAsync(ModelRequest request, CancellationToken ct = default)
    {
        var apiKey = await _keyResolver.ResolveAsync(_settings.ApiKeySecretName, ct);
        var body = BuildRequestBody(request, stream: false);
        using var httpRequest = BuildHttpRequest(apiKey, body);
        using var response = await _http.SendAsync(httpRequest, ct);

        if (!response.IsSuccessStatusCode)
            throw new ModelProviderException("Anthropic", (int)response.StatusCode, $"Anthropic returned {(int)response.StatusCode}");

        var anthropicResponse = await response.Content.ReadFromJsonAsync<AnthropicResponse>(JsonOptions, ct)
            ?? throw new ModelProviderException("Anthropic", null, "Anthropic returned empty response");

        return MapResult(anthropicResponse);
    }

    public async IAsyncEnumerable<ModelChunk> StreamAsync(
        ModelRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var apiKey = await _keyResolver.ResolveAsync(_settings.ApiKeySecretName, ct);
        var body = BuildRequestBody(request, stream: true);
        using var httpRequest = BuildHttpRequest(apiKey, body);
        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
            throw new ModelProviderException("Anthropic", (int)response.StatusCode, $"Anthropic returned {(int)response.StatusCode}");

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var inputTokens = 0;
        string? currentEventType = null;

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                currentEventType = line["event: ".Length..].Trim();
                continue;
            }

            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var data = line["data: ".Length..];
            var (chunk, newInputTokens) = ParseStreamEvent(data, currentEventType, inputTokens);
            inputTokens = newInputTokens;

            if (chunk is not null)
                yield return chunk;
        }
    }

    private AnthropicRequest BuildRequestBody(ModelRequest request, bool stream)
    {
        var messages = new[] { new AnthropicMessage("user", request.Prompt) };
        var maxTokens = request.Options.MaxTokens ?? 4096;
        return new AnthropicRequest(
            _settings.Model,
            maxTokens,
            messages,
            stream ? true : null);
    }

    internal HttpRequestMessage BuildHttpRequest(string apiKey, AnthropicRequest body)
    {
        var url = string.IsNullOrEmpty(_settings.ApiVersion)
            ? $"{_settings.BaseUrl}/v1/messages"
            : $"{_settings.BaseUrl}/v1/messages?api-version={_settings.ApiVersion}";
        var message = new HttpRequestMessage(HttpMethod.Post, url);
        message.Headers.Add("api-key", apiKey);
        message.Headers.Add("anthropic-version", _settings.AnthropicVersion);
        message.Content = JsonContent.Create(body, options: JsonOptions);
        return message;
    }

    private static ModelResult MapResult(AnthropicResponse response)
    {
        var content = response.Content.Count > 0
            ? response.Content[0].Text ?? string.Empty
            : string.Empty;

        var totalTokens = response.Usage.InputTokens + response.Usage.OutputTokens;
        var usage = new TokenUsage(response.Usage.InputTokens, response.Usage.OutputTokens, totalTokens);

        return new ModelResult("Anthropic", response.Model, content, null, usage);
    }

    private static (ModelChunk? Chunk, int InputTokens) ParseStreamEvent(
        string data, string? eventType, int inputTokens)
    {
        AnthropicStreamEvent? evt;
        try { evt = JsonSerializer.Deserialize<AnthropicStreamEvent>(data, JsonOptions); }
        catch (JsonException) { return (null, inputTokens); }
        if (evt is null) return (null, inputTokens);

        return evt.Type switch
        {
            "message_start" => HandleMessageStart(evt, inputTokens),
            "content_block_delta" => HandleContentBlockDelta(evt, inputTokens),
            "message_delta" => HandleMessageDelta(evt, inputTokens),
            "message_stop" => (new ModelChunk(string.Empty, true), inputTokens),
            _ => (null, inputTokens)
        };
    }

    private static (ModelChunk? Chunk, int InputTokens) HandleMessageStart(AnthropicStreamEvent evt, int inputTokens)
    {
        var tokens = evt.Message?.Usage?.InputTokens ?? inputTokens;
        return (null, tokens);
    }

    private static (ModelChunk? Chunk, int InputTokens) HandleContentBlockDelta(AnthropicStreamEvent evt, int inputTokens)
    {
        if (evt.Delta?.Type != "text_delta") return (null, inputTokens);
        return (new ModelChunk(evt.Delta.Text ?? string.Empty, false), inputTokens);
    }

    private static (ModelChunk? Chunk, int InputTokens) HandleMessageDelta(AnthropicStreamEvent evt, int inputTokens)
    {
        if (evt.Usage is null) return (null, inputTokens);
        var total = inputTokens + evt.Usage.OutputTokens;
        var usage = new TokenUsage(inputTokens, evt.Usage.OutputTokens, total);
        return (new ModelChunk(string.Empty, false, usage), inputTokens);
    }
}
