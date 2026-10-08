using System.Text.Json;

namespace Collector.Application.Knowledge;

internal static class EmbeddedPromptResource
{
    public static string Read(string fileName)
    {
        var assembly = typeof(EmbeddedPromptResource).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(candidate => candidate.EndsWith(fileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded prompt resource '{fileName}' is missing.");
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded prompt resource '{fileName}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static List<string> ReadStrings(JsonElement parent, string name) =>
        [.. parent.GetProperty(name).EnumerateArray().Select(item => item.GetString()
            ?? throw new InvalidOperationException($"Prompt resource has a null entry in '{name}'."))];

    public static string RequiredString(JsonElement parent, string name) =>
        parent.GetProperty(name).GetString()
            ?? throw new InvalidOperationException($"Prompt resource property '{name}' is null.");
}
