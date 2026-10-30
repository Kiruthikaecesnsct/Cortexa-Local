using System.Text.Json;
using Collector.Application.Ports;
using Collector.Infrastructure.Options;

namespace Collector.Infrastructure.Ai;

internal static class VertexRequestFactory
{
    public const string UserRole = "user";

    public const string JsonMimeType = "application/json";

    public static VertexGenerateContentRequestWire Create(AiRequest request, VertexProviderOptions settings)
    {
        var contents = new List<VertexContentWire> { new(UserRole, [new VertexPartWire(request.UserText)]) };
        var systemInstruction = new VertexContentWire(null, [new VertexPartWire(request.SystemText)]);
        var generationConfig = new VertexGenerationConfigWire(
            settings.MaxOutputTokens,
            JsonMimeType,
            ParseSchema(request.OutputSchemaJson),
            settings.Temperature);
        return new VertexGenerateContentRequestWire(contents, systemInstruction, generationConfig);
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
}
