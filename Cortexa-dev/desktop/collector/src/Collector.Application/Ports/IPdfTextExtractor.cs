using Collector.Application.Extraction;

namespace Collector.Application.Ports;

public interface IPdfTextExtractor
{
    Task<ParsedDocument> ExtractAsync(string filePath, CancellationToken cancellationToken);
}
