using System.Reflection;
using System.Text.Json;

namespace Collector.Application.Knowledge;

public static class KnowledgePromptLoader
{
    private const string PromptResource = "knowledge-prompt.v1.json";
    private const string SchemaResource = "knowledge-output.schema.json";
    private const string GuardResource = "guard-text.v1.json";

    public static KnowledgePrompt Load()
    {
        using var prompt = JsonDocument.Parse(ReadResource(PromptResource));
        using var guard = JsonDocument.Parse(ReadResource(GuardResource));
        return new KnowledgePrompt
        {
            Version = RequiredString(prompt.RootElement, "version"),
            SystemText = RequiredString(prompt.RootElement.GetProperty("how"), "system"),
            SchemaJson = ReadResource(SchemaResource),
            Guard = ReadGuard(guard.RootElement.GetProperty("how")),
        };
    }

    private static GuardText ReadGuard(JsonElement how) => new()
    {
        Header = RequiredString(how, "header"),
        BeginMarker = RequiredString(how, "beginMarker"),
        EndMarker = RequiredString(how, "endMarker"),
        LineSeparator = RequiredString(how, "lineSeparator"),
        ZeroWidthSpace = RequiredString(how, "zeroWidthSpace"),
        SecurityLines = ReadStrings(how, "securityLines"),
        EscapeTokens = ReadStrings(how, "escapeTokens"),
        HeaderMarkers = ReadStrings(how, "headerMarkers"),
        ProseSubstitutions = [.. how.GetProperty("proseSubstitutions").EnumerateArray()
            .Select(item => new ProseSubstitution(RequiredString(item, "from"), RequiredString(item, "to")))],
    };

    private static List<string> ReadStrings(JsonElement parent, string name) =>
        [.. parent.GetProperty(name).EnumerateArray().Select(item => item.GetString()
            ?? throw new InvalidOperationException($"Prompt resource has a null entry in '{name}'."))];

    private static string RequiredString(JsonElement parent, string name) =>
        parent.GetProperty(name).GetString()
            ?? throw new InvalidOperationException($"Prompt resource property '{name}' is null.");

    private static string ReadResource(string fileName)
    {
        var assembly = typeof(KnowledgePromptLoader).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(candidate => candidate.EndsWith(fileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded prompt resource '{fileName}' is missing.");
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded prompt resource '{fileName}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
