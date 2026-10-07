using Collector.Infrastructure.Extraction;
using Collector.Tests.Support;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Collector.Tests.Extraction;

public sealed class OpenXmlTextExtractorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"collector-docx-{Guid.NewGuid():N}");
    private readonly OpenXmlTextExtractor _extractor = new();

    public OpenXmlTextExtractorTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string BuildDocx(IReadOnlyList<string> paragraphTexts)
    {
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.docx");
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = new Body();
        foreach (var text in paragraphTexts)
        {
            body.Append(new Paragraph(new Run(new Text(text))));
        }

        mainPart.Document.Append(body);
        mainPart.Document.Save();
        return path;
    }

    [Fact]
    public async Task Extracts_paragraph_text_joined_by_newlines()
    {
        var path = BuildDocx(["First paragraph.", "Second paragraph.", "Third paragraph."]);

        var parsed = await _extractor.ExtractAsync(path, TestSupport.Ct);

        Assert.Equal("First paragraph.\nSecond paragraph.\nThird paragraph.", parsed.Text);
        Assert.Empty(parsed.Pages);
    }

    [Fact]
    public async Task Returns_empty_text_for_a_document_with_no_paragraphs()
    {
        var path = BuildDocx([]);

        var parsed = await _extractor.ExtractAsync(path, TestSupport.Ct);

        Assert.Equal(string.Empty, parsed.Text);
    }
}
