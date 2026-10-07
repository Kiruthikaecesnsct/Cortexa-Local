using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Collector.Application.Knowledge;

namespace Collector.Tests.Knowledge;

public sealed partial class KnowledgePromptLoaderTests
{
    private static JsonDocument ReadPromptResource()
    {
        var assembly = typeof(KnowledgePromptLoader).Assembly;
        var name = assembly.GetManifestResourceNames().Single(candidate => candidate.EndsWith("knowledge-prompt.v1.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        return JsonDocument.Parse(stream);
    }

    [GeneratedRegex("title '([^']+)'")]
    private static partial Regex QuotedTitle();

    [Fact]
    public void Load_Always_VersionIsKnowledgeV1()
    {
        var prompt = KnowledgePromptLoader.Load();

        Assert.Equal("knowledge.v1", prompt.Version);
    }

    [Fact]
    public void Load_Always_SystemTextEqualsHowSystemByteForByte()
    {
        using var document = ReadPromptResource();
        var expected = document.RootElement.GetProperty("how").GetProperty("system").GetString()!;

        var prompt = KnowledgePromptLoader.Load();

        Assert.Equal(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(prompt.SystemText));
    }

    [Fact]
    public void Load_ExampleTitles_AppearInSystemText()
    {
        using var document = ReadPromptResource();
        var outputs = document.RootElement.GetProperty("how").GetProperty("examples")
            .EnumerateArray()
            .Select(example => example.GetProperty("output").GetString()!);
        var titles = outputs.SelectMany(output => QuotedTitle().Matches(output).Select(match => match.Groups[1].Value)).ToList();

        var prompt = KnowledgePromptLoader.Load();

        Assert.NotEmpty(titles);
        Assert.All(titles, title => Assert.Contains(title, prompt.SystemText));
    }

    [Fact]
    public void Load_Always_SchemaIsObjectRequiringItems()
    {
        var prompt = KnowledgePromptLoader.Load();

        using var schema = JsonDocument.Parse(prompt.SchemaJson);
        var required = schema.RootElement.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToList();

        Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        Assert.Equal(["items"], required);
    }

    [Fact]
    public void Load_Always_GuardCarriesMarkers()
    {
        var prompt = KnowledgePromptLoader.Load();

        Assert.Equal("<<<CORTEXA_UNTRUSTED_BEGIN>>>", prompt.Guard.BeginMarker);
        Assert.Equal("<<<CORTEXA_UNTRUSTED_END>>>", prompt.Guard.EndMarker);
    }
}
