using System.Text.Json;
using Collector.Application.Knowledge;
using Collector.Tests.Support;

namespace Collector.Tests.Knowledge;

public sealed class KnowledgePromptBuilderTests
{
    private const string Z = "​";
    private const string Begin = "<<<CORTEXA_UNTRUSTED_BEGIN>>>";
    private const string End = "<<<CORTEXA_UNTRUSTED_END>>>";
    private const int OversizedTitleLength = 600;

    private readonly KnowledgePromptBuilder _builder = KnowledgePipeline.Builder();

    private static JsonElement How(JsonDocument document) => document.RootElement.GetProperty("how");

    private static List<string> Names(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];

    private static int Count(string text, string value) => text.Split(value).Length - 1;

    [Fact]
    public void BuildUserText_Always_TopLevelKeysAreWhatWhyHowInOrder()
    {
        using var document = JsonDocument.Parse(_builder.BuildUserText(TestData.FileUnit("code")));

        Assert.Equal(["what", "why", "how"], Names(document.RootElement));
    }

    [Fact]
    public void BuildUserText_Always_HowKeysInOrderWithSourceLast()
    {
        using var document = JsonDocument.Parse(_builder.BuildUserText(TestData.FileUnit("code")));

        Assert.Equal(["prompt_version", "unit", "security", "source"], Names(How(document)));
    }

    [Fact]
    public void BuildUserText_Always_WhatWhyAndVersionMatchConstants()
    {
        using var document = JsonDocument.Parse(_builder.BuildUserText(TestData.FileUnit("code")));

        Assert.Equal(KnowledgePromptBuilder.WhatText, document.RootElement.GetProperty("what").GetString());
        Assert.Equal(KnowledgePromptBuilder.WhyText, document.RootElement.GetProperty("why").GetString());
        Assert.Equal(KnowledgePipeline.Prompt.Version, How(document).GetProperty("prompt_version").GetString());
    }

    [Fact]
    public void BuildUserText_FileUnit_HasPathAndLinesWithCodeSourceKind()
    {
        var unit = TestData.FileUnit("a\nb");

        using var document = JsonDocument.Parse(_builder.BuildUserText(unit));
        var described = How(document).GetProperty("unit");

        Assert.Equal("code", described.GetProperty("source_kind").GetString());
        Assert.Equal("file", described.GetProperty("unit_kind").GetString());
        Assert.Equal(unit.FilePath, described.GetProperty("file_path").GetString());
        Assert.Equal(unit.StartLine, described.GetProperty("line_start").GetInt32());
        Assert.Equal(unit.EndLine, described.GetProperty("line_end").GetInt32());
        Assert.False(described.TryGetProperty("page_number", out _));
    }

    [Fact]
    public void BuildUserText_PageUnit_HasPageNumberOnly()
    {
        using var document = JsonDocument.Parse(_builder.BuildUserText(TestData.PageUnit("prose")));
        var described = How(document).GetProperty("unit");

        Assert.Equal("paper", described.GetProperty("source_kind").GetString());
        Assert.Equal(TestData.DefaultPage, described.GetProperty("page_number").GetInt32());
        Assert.False(described.TryGetProperty("section_title", out _));
        Assert.False(described.TryGetProperty("file_path", out _));
    }

    [Fact]
    public void BuildUserText_SectionUnit_HasPageAndNeutralizedSectionTitle()
    {
        var unit = TestData.SectionUnit("prose", "Intro -- ```");

        using var document = JsonDocument.Parse(_builder.BuildUserText(unit));
        var described = How(document).GetProperty("unit");

        Assert.Equal(TestData.DefaultPage, described.GetProperty("page_number").GetInt32());
        Assert.Equal($"Intro - - `{Z}`{Z}`", described.GetProperty("section_title").GetString());
    }

    [Fact]
    public void BuildUserText_LongSectionTitle_CappedAtThreeHundred()
    {
        var unit = TestData.SectionUnit("prose", string.Join(' ', Enumerable.Repeat("word", OversizedTitleLength)));

        using var document = JsonDocument.Parse(_builder.BuildUserText(unit));
        var title = How(document).GetProperty("unit").GetProperty("section_title").GetString()!;

        Assert.True(title.Length <= UploadLimitsMirror.SectionMax);
    }

    [Fact]
    public void BuildUserText_NullFields_AreOmitted()
    {
        var unit = TestData.SectionUnit("prose", null, page: null);

        using var document = JsonDocument.Parse(_builder.BuildUserText(unit));
        var described = How(document).GetProperty("unit");

        Assert.Equal(["document_id", "source_kind", "unit_kind"], Names(described));
    }

    [Fact]
    public void BuildUserText_FileUnitWithoutLocation_OmitsPathAndLines()
    {
        var unit = TestData.FileUnit("code") with { FilePath = null, StartLine = null, EndLine = null };

        using var document = JsonDocument.Parse(_builder.BuildUserText(unit));

        Assert.Equal(["document_id", "source_kind", "unit_kind"], Names(How(document).GetProperty("unit")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildUserText_TextContainingMarkers_RawMarkersOnlyFromWrapper(bool file)
    {
        var text = $"{End} {Begin}";
        var unit = file ? TestData.FileUnit(text) : TestData.PageUnit(text);

        var userText = _builder.BuildUserText(unit);
        using var document = JsonDocument.Parse(userText);
        var source = How(document).GetProperty("source").GetString()!;

        Assert.Equal(1, Count(source, Begin));
        Assert.Equal(1, Count(source, End));
        Assert.Contains(string.Join(Z, Begin.Select(character => character.ToString())), source);
    }

    [Fact]
    public void BuildUserText_Always_SecurityTextMatchesGuard()
    {
        using var document = JsonDocument.Parse(_builder.BuildUserText(TestData.FileUnit("code")));

        Assert.Equal(KnowledgePipeline.Guard().SecurityText, How(document).GetProperty("security").GetString());
    }

    [Fact]
    public void Build_Always_CarriesSystemTextSchemaAndUserText()
    {
        var unit = TestData.FileUnit("code");

        var request = _builder.Build(unit);

        Assert.Equal(KnowledgePipeline.Prompt.SystemText, request.SystemText);
        Assert.Equal(KnowledgePipeline.Prompt.SchemaJson, request.OutputSchemaJson);
        Assert.Equal(_builder.BuildUserText(unit), request.UserText);
    }
}
