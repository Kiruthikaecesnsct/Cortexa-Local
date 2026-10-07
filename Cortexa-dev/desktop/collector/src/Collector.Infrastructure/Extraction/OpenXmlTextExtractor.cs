using Collector.Application.Extraction;
using Collector.Application.Ports;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Collector.Infrastructure.Extraction;

public sealed class OpenXmlTextExtractor : IDocxTextExtractor
{
    public Task<ParsedDocument> ExtractAsync(string filePath, CancellationToken cancellationToken) =>
        Task.Run(() => Extract(filePath, cancellationToken), cancellationToken);

    private static ParsedDocument Extract(string filePath, CancellationToken cancellationToken)
    {
        using var document = WordprocessingDocument.Open(filePath, isEditable: false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return new ParsedDocument { Text = string.Empty };
        }

        var paragraphs = body.Elements<Paragraph>()
            .Select(paragraph =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return paragraph.InnerText;
            });

        return new ParsedDocument { Text = string.Join('\n', paragraphs) };
    }
}
