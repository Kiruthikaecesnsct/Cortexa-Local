using System.Text.Json;
using Collector.Application.Ports;
using Collector.Infrastructure.Options;
using Google.GenAI.Types;

namespace Collector.Infrastructure.Ai;

public sealed record GeminiRequest(List<Content> Contents, GenerateContentConfig Config);

public static class GeminiRequestFactory
{
    public const string JsonMimeType = "application/json";

    public const string UserRole = "user";

    public static GeminiRequest Create(AiRequest request, GeminiProviderOptions settings)
    {
        var contents = new List<Content> { new() { Role = UserRole, Parts = [new Part { Text = request.UserText }] } };
        var config = new GenerateContentConfig
        {
            SystemInstruction = new Content { Parts = [new Part { Text = request.SystemText }] },
            MaxOutputTokens = settings.MaxOutputTokens,
            ResponseMimeType = JsonMimeType,
            ResponseJsonSchema = ParseSchema(request.OutputSchemaJson),
            ThinkingConfig = BuildThinking(settings.Thinking),
            Temperature = settings.Temperature,
        };
        return new GeminiRequest(contents, config);
    }

    private static JsonElement ParseSchema(string schemaJson)
    {
        using var document = JsonDocument.Parse(schemaJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Output schema must be a JSON object.", nameof(schemaJson));
        }

        return document.RootElement.Clone();
    }

    private static ThinkingConfig? BuildThinking(string level) => level switch
    {
        GeminiProviderOptions.ThinkingLow => new ThinkingConfig { ThinkingLevel = ThinkingLevel.Low },
        GeminiProviderOptions.ThinkingMedium => new ThinkingConfig { ThinkingLevel = ThinkingLevel.Medium },
        GeminiProviderOptions.ThinkingHigh => new ThinkingConfig { ThinkingLevel = ThinkingLevel.High },
        _ => null,
    };
}
