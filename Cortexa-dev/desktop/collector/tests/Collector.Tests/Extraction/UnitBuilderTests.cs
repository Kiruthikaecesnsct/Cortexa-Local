using Collector.Application.Extraction;
using Collector.Domain.Enums;

namespace Collector.Tests.Extraction;

public sealed class UnitBuilderTests
{
    private readonly UnitBuilder _builder = new(new HeadingDetector());

    [Fact]
    public void Builds_one_page_unit_per_page_span_with_page_numbers()
    {
        const string text = "Page one content.Page two content.";
        var parsed = new ParsedDocument
        {
            Text = text,
            Pages =
            [
                new PageSpan { PageNumber = 1, StartOffset = 0, EndOffset = 17 },
                new PageSpan { PageNumber = 2, StartOffset = 17, EndOffset = 34 },
            ],
        };

        var drafts = _builder.BuildFromPages(parsed);

        Assert.Equal(2, drafts.Count);
        Assert.All(drafts, d => Assert.Equal(UnitKind.Page, d.UnitKind));
        Assert.Equal(1, drafts[0].PageNumber);
        Assert.Equal(2, drafts[1].PageNumber);
        Assert.Equal("Page one content.", drafts[0].Text);
        Assert.Equal("Page two content.", drafts[1].Text);
    }

    [Fact]
    public void Builds_whole_document_as_file_unit_when_no_pages_present()
    {
        var parsed = new ParsedDocument { Text = "no page spans here" };

        var drafts = _builder.BuildFromPages(parsed);

        Assert.Single(drafts);
        Assert.Equal(UnitKind.File, drafts[0].UnitKind);
    }

    [Fact]
    public void Builds_section_units_aligned_to_detected_headings()
    {
        const string text = "# First\nbody one text here.\n# Second\nbody two text here.";

        var drafts = _builder.BuildFromSections(text);

        Assert.Equal(2, drafts.Count);
        Assert.All(drafts, d => Assert.Equal(UnitKind.Section, d.UnitKind));
        Assert.Equal("First", drafts[0].SectionTitle);
        Assert.Equal("Second", drafts[1].SectionTitle);
        Assert.Contains("body one text here.", drafts[0].Text);
        Assert.Contains("body two text here.", drafts[1].Text);
    }

    [Fact]
    public void Builds_single_file_unit_when_text_has_no_headings()
    {
        const string text = "plain text with no structure at all.";

        var drafts = _builder.BuildFromSections(text);

        Assert.Single(drafts);
        Assert.Equal(UnitKind.File, drafts[0].UnitKind);
        Assert.Equal(text, drafts[0].Text);
    }

    [Fact]
    public void Builds_module_units_from_code_splitter_output_with_file_path_and_lines()
    {
        var codeUnits = new List<CodeUnit>
        {
            new() { Text = "void A() {}", StartLine = 1, EndLine = 1 },
            new() { Text = "void B() {}", StartLine = 3, EndLine = 3 },
        };

        var drafts = _builder.BuildFromCode(codeUnits, "src/Foo.cs");

        Assert.Equal(2, drafts.Count);
        Assert.All(drafts, d => Assert.Equal(UnitKind.Module, d.UnitKind));
        Assert.All(drafts, d => Assert.Equal("src/Foo.cs", d.FilePath));
        Assert.Equal(1, drafts[0].StartLine);
        Assert.Equal(3, drafts[1].StartLine);
    }
}
