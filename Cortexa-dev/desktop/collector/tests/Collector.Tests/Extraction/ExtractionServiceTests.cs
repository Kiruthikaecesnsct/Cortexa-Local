using Collector.Application.Extraction;
using Collector.Domain.Enums;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Tests.Extraction;

public sealed class ExtractionServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"collector-extraction-{Guid.NewGuid():N}");
    private readonly FakePdfTextExtractor _pdfExtractor = new();
    private readonly FakeDocxTextExtractor _docxExtractor = new();
    private readonly WordCountTokenCounter _tokenCounter = new();
    private readonly InMemoryDocumentStore _documentStore = new();
    private readonly InMemoryUnitStore _unitStore = new();
    private readonly ExtractionService _service;

    public ExtractionServiceTests()
    {
        Directory.CreateDirectory(_directory);
        _service = new ExtractionService(
            _pdfExtractor,
            _docxExtractor,
            _tokenCounter,
            _documentStore,
            _unitStore,
            new FileContentGuard(),
            new TextNormalizer(),
            new CodeSplitter(_tokenCounter),
            new UnitBuilder(new HeadingDetector()),
            new DocumentStatusRules(),
            NullLogger<ExtractionService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string WriteFile(string name, byte[] content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public async Task Skips_a_code_file_larger_than_one_mebibyte_with_a_clear_reason()
    {
        var path = WriteFile("Huge.cs", new byte[FileContentGuard.MaxFileBytes + 1]);

        var results = await _service.ExtractAsync([path], SourceType.Local, SourceKind.Code, TestSupport.Ct);

        Assert.Single(results);
        Assert.Equal(DocumentStatus.Excluded, results[0].Status);
        Assert.Equal(nameof(SkipReason.TooLarge), results[0].Reason);
        Assert.Empty(_unitStore.ByDocumentId);
    }

    [Fact]
    public async Task Extracts_a_plain_text_file_into_units_and_marks_document_extracted()
    {
        var path = WriteFile("notes.txt", System.Text.Encoding.UTF8.GetBytes("# Heading\nsome body content here."));

        var results = await _service.ExtractAsync([path], SourceType.Local, SourceKind.Paper, TestSupport.Ct);

        Assert.Single(results);
        Assert.Equal(DocumentStatus.Extracted, results[0].Status);
        var units = _unitStore.ByDocumentId[results[0].DocumentId!];
        Assert.NotEmpty(units);
        Assert.All(units, u => Assert.True(u.TokenCount >= 0));
    }

    [Fact]
    public async Task Extracts_a_multi_page_pdf_with_page_numbers_recorded_on_each_unit()
    {
        _pdfExtractor.Result = new ParsedDocument
        {
            Text = "Page one body.Page two body.",
            Pages =
            [
                new PageSpan { PageNumber = 1, StartOffset = 0, EndOffset = 14 },
                new PageSpan { PageNumber = 2, StartOffset = 14, EndOffset = 29 },
            ],
        };
        var path = WriteFile("doc.pdf", [0x25, 0x50, 0x44, 0x46]);

        var results = await _service.ExtractAsync([path], SourceType.Local, SourceKind.Paper, TestSupport.Ct);

        Assert.Equal(DocumentStatus.Extracted, results[0].Status);
        var units = _unitStore.ByDocumentId[results[0].DocumentId!];
        Assert.Equal(2, units.Count);
        Assert.Equal(1, units[0].PageNumber);
        Assert.Equal(2, units[1].PageNumber);
    }

    [Fact]
    public async Task Marks_document_failed_when_parsing_throws()
    {
        _docxExtractor.Result = null!;
        var path = WriteFile("broken.docx", [0x50, 0x4b]);

        var results = await _service.ExtractAsync([path], SourceType.Local, SourceKind.Paper, TestSupport.Ct);

        Assert.Equal(DocumentStatus.Failed, results[0].Status);
        Assert.NotNull(results[0].Reason);
    }

    [Fact]
    public async Task Returns_failed_result_for_a_missing_file_and_still_extracts_the_rest_of_the_batch()
    {
        var missing = Path.Combine(_directory, "missing.txt");
        var present = WriteFile("notes.txt", "plain notes text"u8.ToArray());

        var results = await _service.ExtractAsync([missing, present], SourceType.Local, SourceKind.Paper, TestSupport.Ct);

        Assert.Equal(2, results.Count);
        Assert.Equal(DocumentStatus.Failed, results[0].Status);
        Assert.Null(results[0].DocumentId);
        Assert.NotNull(results[0].Reason);
        Assert.Equal(DocumentStatus.Extracted, results[1].Status);
    }
}
