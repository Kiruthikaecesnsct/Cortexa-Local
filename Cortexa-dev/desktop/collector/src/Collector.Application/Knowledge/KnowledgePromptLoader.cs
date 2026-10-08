using System.Text.Json;

namespace Collector.Application.Knowledge;

public static class KnowledgePromptLoader
{
    private const string PromptResource = "knowledge-prompt.v1.json";
    private const string SchemaResource = "knowledge-output.schema.json";
    private const string GuardResource = "guard-text.v1.json";

    public static KnowledgePrompt Load()
    {
        using var prompt = JsonDocument.Parse(EmbeddedPromptResource.Read(PromptResource));
        return new KnowledgePrompt
        {
            Version = EmbeddedPromptResource.RequiredString(prompt.RootElement, "version"),
            SystemText = EmbeddedPromptResource.RequiredString(prompt.RootElement.GetProperty("how"), "system"),
            SchemaJson = EmbeddedPromptResource.Read(SchemaResource),
            Guard = LoadGuard(),
        };
    }

    public static GuardText LoadGuard()
    {
        using var guard = JsonDocument.Parse(EmbeddedPromptResource.Read(GuardResource));
        return ReadGuard(guard.RootElement.GetProperty("how"));
    }

    private static GuardText ReadGuard(JsonElement how) => new()
    {
        Header = EmbeddedPromptResource.RequiredString(how, "header"),
        BeginMarker = EmbeddedPromptResource.RequiredString(how, "beginMarker"),
        EndMarker = EmbeddedPromptResource.RequiredString(how, "endMarker"),
        LineSeparator = EmbeddedPromptResource.RequiredString(how, "lineSeparator"),
        ZeroWidthSpace = EmbeddedPromptResource.RequiredString(how, "zeroWidthSpace"),
        SecurityLines = EmbeddedPromptResource.ReadStrings(how, "securityLines"),
        EscapeTokens = EmbeddedPromptResource.ReadStrings(how, "escapeTokens"),
        HeaderMarkers = EmbeddedPromptResource.ReadStrings(how, "headerMarkers"),
        ProseSubstitutions = [.. how.GetProperty("proseSubstitutions").EnumerateArray()
            .Select(item => new ProseSubstitution(
                EmbeddedPromptResource.RequiredString(item, "from"),
                EmbeddedPromptResource.RequiredString(item, "to")))],
    };
}
