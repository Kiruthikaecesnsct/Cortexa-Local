using System.Text.Json;
using Collector.Application.Knowledge;
using Collector.Domain.Knowledge;
using Collector.Tests.Support;

namespace Collector.Tests.Knowledge;

public sealed class LayerPromptBuilderTests
{
    private const int ManyFiles = 60;

    private readonly LayerPromptBuilder _builder = LayerPipeline.Builder();

    private static JsonElement How(JsonDocument document) => document.RootElement.GetProperty("how");

    private static List<string> Names(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];

    private static FolderGroup GroupOf(int fileCount, string folder = "src/app")
    {
        var files = Enumerable.Range(0, fileCount)
            .Select(index => new FolderFile(
                $"{folder}/file{index:D3}.cs",
                [TestData.Item($"Idea {index}", summary: $"Summary {index}") with
                {
                    Source = new KnowledgeSource { FilePath = $"{folder}/file{index:D3}.cs", LineStart = 1, LineEnd = 2 },
                }]))
            .ToList();
        return new FolderGroup(folder, files);
    }

    [Fact]
    public void BuildUserText_Always_TopLevelKeysAreWhatWhyHowInOrder()
    {
        using var document = JsonDocument.Parse(_builder.BuildUserText(GroupOf(3)));

        Assert.Equal(["what", "why", "how"], Names(document.RootElement));
    }

    [Fact]
    public void BuildUserText_Always_HowKeysInOrderWithSourceLast()
    {
        using var document = JsonDocument.Parse(_builder.BuildUserText(GroupOf(3)));

        Assert.Equal(["prompt_version", "folder", "security", "source"], Names(How(document)));
    }

    [Fact]
    public void BuildUserText_SmallFolder_FolderMetadataMatchesFileCount()
    {
        using var document = JsonDocument.Parse(_builder.BuildUserText(GroupOf(3)));
        var folder = How(document).GetProperty("folder");

        Assert.Equal("src/app", folder.GetProperty("path").GetString());
        Assert.Equal(3, folder.GetProperty("file_count").GetInt32());
        Assert.Equal(3, folder.GetProperty("total_file_count").GetInt32());
        Assert.False(folder.GetProperty("files_truncated").GetBoolean());
    }

    [Fact]
    public void BuildUserText_MoreThanFiftyFiles_IsCappedAndTruncated()
    {
        using var document = JsonDocument.Parse(_builder.BuildUserText(GroupOf(ManyFiles)));
        var folder = How(document).GetProperty("folder");

        Assert.Equal(50, folder.GetProperty("file_count").GetInt32());
        Assert.Equal(ManyFiles, folder.GetProperty("total_file_count").GetInt32());
        Assert.True(folder.GetProperty("files_truncated").GetBoolean());
    }

    [Fact]
    public void BuildUserText_Always_SecurityTextMatchesGuard()
    {
        using var document = JsonDocument.Parse(_builder.BuildUserText(GroupOf(3)));

        Assert.Equal(KnowledgePipeline.Guard().SecurityText, How(document).GetProperty("security").GetString());
    }

    [Fact]
    public void BuildUserText_Source_ContainsEachFileTitleAndSummary()
    {
        using var document = JsonDocument.Parse(_builder.BuildUserText(GroupOf(3)));
        var source = How(document).GetProperty("source").GetString()!;

        Assert.Contains("Idea 0", source);
        Assert.Contains("Summary 0", source);
        Assert.Contains("file000.cs", source);
    }

    [Fact]
    public void Build_Always_CarriesSystemTextSchemaAndUserText()
    {
        var group = GroupOf(3);

        var request = _builder.Build(group);

        Assert.Equal(LayerPipeline.Prompt.SystemText, request.SystemText);
        Assert.Equal(LayerPipeline.Prompt.SchemaJson, request.OutputSchemaJson);
        Assert.Equal(_builder.BuildUserText(group), request.UserText);
    }
}
