using Collector.Domain.Enums;
using Collector.Domain.Extraction;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class UnitPreviewItemViewModelTests
{
    private const string DocumentPath = @"C:\papers\thesis.pdf";

    [Fact]
    public void Labels_a_page_unit_with_its_page_number()
    {
        var unit = Unit(UnitKind.Page, pageNumber: 3);

        var label = UnitPreviewItemViewModel.BoundaryLabelFor(unit, DocumentPath);

        Assert.Equal("Page 3", label);
    }

    [Fact]
    public void Labels_a_section_unit_with_its_heading()
    {
        var unit = Unit(UnitKind.Section, sectionTitle: "Introduction");

        var label = UnitPreviewItemViewModel.BoundaryLabelFor(unit, DocumentPath);

        Assert.Equal("Introduction", label);
    }

    [Fact]
    public void Labels_a_file_unit_with_the_documents_source_path_when_no_file_path_is_recorded()
    {
        var unit = Unit(UnitKind.File);

        var label = UnitPreviewItemViewModel.BoundaryLabelFor(unit, DocumentPath);

        Assert.Equal(DocumentPath, label);
    }

    [Fact]
    public void Labels_a_file_unit_with_a_line_window_when_the_token_window_fallback_set_lines()
    {
        var unit = Unit(UnitKind.File, filePath: @"C:\code\big.cs", startLine: 1, endLine: 80);

        var label = UnitPreviewItemViewModel.BoundaryLabelFor(unit, DocumentPath);

        Assert.Equal(@"C:\code\big.cs · lines 1-80 (window)", label);
    }

    [Fact]
    public void Labels_a_module_unit_with_its_line_range()
    {
        var unit = Unit(UnitKind.Module, startLine: 10, endLine: 42);

        var label = UnitPreviewItemViewModel.BoundaryLabelFor(unit, DocumentPath);

        Assert.Equal("lines 10-42", label);
    }

    [Fact]
    public void Labels_a_module_unit_with_its_symbol_name_when_one_is_recorded()
    {
        var unit = Unit(UnitKind.Module, sectionTitle: "ParseFile", startLine: 10, endLine: 42);

        var label = UnitPreviewItemViewModel.BoundaryLabelFor(unit, DocumentPath);

        Assert.Equal("ParseFile · lines 10-42", label);
    }

    [Theory]
    [InlineData(UnitKind.Page, "Page")]
    [InlineData(UnitKind.Section, "Section")]
    [InlineData(UnitKind.File, "File")]
    [InlineData(UnitKind.Module, "Module")]
    public void Maps_unit_kind_to_its_chip_label(UnitKind kind, string expected)
    {
        Assert.Equal(expected, UnitPreviewItemViewModel.KindLabelFor(kind));
    }

    [Fact]
    public void Exposes_the_token_count_from_the_unit()
    {
        var item = new UnitPreviewItemViewModel(Unit(UnitKind.Page, pageNumber: 1, tokenCount: 128), DocumentPath);

        Assert.Equal(128, item.TokenCount);
    }

    [Fact]
    public void Truncates_long_snippets_with_an_ellipsis()
    {
        var longText = new string('a', 500);
        var item = new UnitPreviewItemViewModel(Unit(UnitKind.Page, pageNumber: 1, text: longText), DocumentPath);

        Assert.True(item.Snippet.Length <= 321);
        Assert.EndsWith("…", item.Snippet);
    }

    [Fact]
    public void Leaves_short_snippets_untouched()
    {
        var item = new UnitPreviewItemViewModel(Unit(UnitKind.Page, pageNumber: 1, text: "short body"), DocumentPath);

        Assert.Equal("short body", item.Snippet);
    }

    private static ExtractionUnit Unit(
        UnitKind kind,
        int? pageNumber = null,
        string? sectionTitle = null,
        string? filePath = null,
        int? startLine = null,
        int? endLine = null,
        string text = "body",
        int tokenCount = 0) =>
        new()
        {
            Id = Guid.NewGuid().ToString("n"),
            DocumentId = "doc-1",
            Ordinal = 0,
            UnitKind = kind,
            PageNumber = pageNumber,
            SectionTitle = sectionTitle,
            FilePath = filePath,
            StartLine = startLine,
            EndLine = endLine,
            Text = text,
            TokenCount = tokenCount,
            Status = DocumentStatus.Extracted,
        };
}
