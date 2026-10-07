using Collector.Infrastructure.Extraction;
using Collector.Tests.Support;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Collector.Tests.Extraction;

public sealed class PdfPigTextExtractorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"collector-pdf-{Guid.NewGuid():N}");
    private readonly PdfPigTextExtractor _extractor = new();

    public PdfPigTextExtractorTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string BuildMultiPagePdf(string[] pageTexts)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var pageText in pageTexts)
        {
            var page = builder.AddPage(PageSize.A4, isPortrait: true);
            page.AddText(pageText, 12, new PdfPoint(25, 700), font);
        }

        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    [Fact]
    public async Task Extracts_text_per_page_with_page_numbers_recorded()
    {
        var path = BuildMultiPagePdf(["First page body", "Second page body", "Third page body"]);

        var parsed = await _extractor.ExtractAsync(path, TestSupport.Ct);

        Assert.Equal(3, parsed.Pages.Count);
        Assert.Equal([1, 2, 3], parsed.Pages.Select(p => p.PageNumber).ToArray());
        Assert.Contains("First page body", parsed.Text);
        Assert.Contains("Second page body", parsed.Text);
        Assert.Contains("Third page body", parsed.Text);
    }

    [Fact]
    public async Task Each_page_span_offsets_map_back_into_the_combined_text()
    {
        var path = BuildMultiPagePdf(["Alpha content here", "Beta content here"]);

        var parsed = await _extractor.ExtractAsync(path, TestSupport.Ct);

        foreach (var page in parsed.Pages)
        {
            var slice = parsed.Text[page.StartOffset..page.EndOffset];
            Assert.NotEmpty(slice);
        }
    }
}
