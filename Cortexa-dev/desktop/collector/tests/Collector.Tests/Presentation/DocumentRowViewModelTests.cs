using Collector.Application.Extraction;
using Collector.Domain.Enums;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class DocumentRowViewModelTests
{
    [Fact]
    public void Derives_filename_from_the_source_path()
    {
        var row = new DocumentRowViewModel(@"C:\papers\thesis.pdf");

        Assert.Equal("thesis.pdf", row.Filename);
    }

    [Theory]
    [InlineData(DocumentStatus.Pending, "Pending")]
    [InlineData(DocumentStatus.Extracting, "Extracting")]
    [InlineData(DocumentStatus.Extracted, "Extracted")]
    [InlineData(DocumentStatus.Failed, "Failed")]
    [InlineData(DocumentStatus.Excluded, "Excluded")]
    public void Maps_status_to_a_human_readable_label(DocumentStatus status, string expected)
    {
        var row = new DocumentRowViewModel(@"C:\papers\thesis.pdf") { Status = status };

        Assert.Equal(expected, row.StatusLabel);
    }

    [Theory]
    [InlineData(DocumentStatus.Pending, false)]
    [InlineData(DocumentStatus.Extracting, false)]
    [InlineData(DocumentStatus.Extracted, false)]
    [InlineData(DocumentStatus.Failed, true)]
    [InlineData(DocumentStatus.Excluded, true)]
    public void Flags_failed_and_excluded_documents_as_skipped(DocumentStatus status, bool expected)
    {
        var row = new DocumentRowViewModel(@"C:\papers\thesis.pdf") { Status = status };

        Assert.Equal(expected, row.IsSkipped);
    }

    [Fact]
    public void Builds_automation_name_from_filename_status_and_unit_count()
    {
        var row = new DocumentRowViewModel(@"C:\papers\thesis.pdf") { Status = DocumentStatus.Extracted };
        row.ApplyUnitTotals(unitCount: 7, tokenCount: 420);

        Assert.Equal("thesis.pdf, Extracted, 7 units", row.AutomationName);
    }

    [Theory]
    [InlineData(nameof(SkipReason.TooLarge), "File is too large to parse.")]
    [InlineData(nameof(SkipReason.BinaryContent), "File content could not be read as text.")]
    [InlineData(nameof(SkipReason.UnsupportedFormat), "This file type isn't supported.")]
    public void Maps_excluded_skip_reasons_to_friendly_text(string reason, string expected)
    {
        Assert.Equal(expected, DocumentRowViewModel.SkipReasonTextFor(DocumentStatus.Excluded, reason));
    }

    [Fact]
    public void Maps_failed_status_to_a_generic_parsing_failure_message()
    {
        Assert.Equal("Parsing failed.", DocumentRowViewModel.SkipReasonTextFor(DocumentStatus.Failed, "boom"));
    }

    [Fact]
    public void Applying_a_result_updates_document_id_status_and_reason()
    {
        var row = new DocumentRowViewModel(@"C:\papers\thesis.pdf");

        row.ApplyResult(new ExtractionResult
        {
            SourcePath = row.SourcePath,
            DocumentId = "doc-1",
            Status = DocumentStatus.Excluded,
            Reason = nameof(SkipReason.TooLarge),
        });

        Assert.Equal("doc-1", row.DocumentId);
        Assert.Equal(DocumentStatus.Excluded, row.Status);
        Assert.Equal("File is too large to parse.", row.SkipReasonText);
    }
}
