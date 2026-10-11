using System.Text.Json;
using Collector.Application.Ports;
using Collector.Infrastructure.Options;

namespace Collector.Infrastructure.Ai;

internal static class GroqRequestFactory
{
    public const string SystemRole = "system";

    public const string UserRole = "user";

    public const string JsonSchemaType = "json_schema";

    private const string SchemaName = "knowledge_extraction";

    public static GroqChatRequestWire Create(AiRequest request, GroqProviderOptions settings)
    {
        var model = string.IsNullOrWhiteSpace(request.Model) ? settings.Model : request.Model;
        var messages = new List<GroqMessageWire>
        {
            new(SystemRole, request.SystemText),
            new(UserRole, request.UserText),
        };
        return new GroqChatRequestWire(
            model,
            messages,
            settings.MaxOutputTokens,
            settings.Temperature,
            BuildResponseFormat(request.OutputSchemaJson));
    }

    private static GroqResponseFormatWire BuildResponseFormat(string schemaJson) =>
        new(JsonSchemaType, new GroqJsonSchemaWire(SchemaName, false, ParseSchema(schemaJson)));

    private static JsonElement ParseSchema(string schemaJson)
    {
        using var document = JsonDocument.Parse(schemaJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Output schema must be a JSON object.", nameof(schemaJson));
        }

        return document.RootElement.Clone();
    }
}
