using System.Text;
using Collector.Application.Extraction;
using Collector.Application.Ports;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Collector.Infrastructure.Extraction;

public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public Task<ParsedDocument> ExtractAsync(string filePath, CancellationToken cancellationToken) =>
        Task.Run(() => Extract(filePath, cancellationToken), cancellationToken);

    private static ParsedDocument Extract(string filePath, CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(filePath);
        var builder = new StringBuilder();
        var pages = new List<PageSpan>(document.NumberOfPages);

        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageText = ContentOrderTextExtractor.GetText(page);
            var start = builder.Length;
            builder.Append(pageText);
            var end = builder.Length;
            pages.Add(new PageSpan { PageNumber = page.Number, StartOffset = start, EndOffset = end });
            builder.Append('\n');
        }

        return new ParsedDocument { Text = builder.ToString(), Pages = pages };
    }
}
