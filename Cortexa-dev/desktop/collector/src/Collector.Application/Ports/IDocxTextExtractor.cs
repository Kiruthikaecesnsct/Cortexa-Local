using Collector.Application.Extraction;

namespace Collector.Application.Ports;

public interface IDocxTextExtractor
{
    Task<ParsedDocument> ExtractAsync(string filePath, CancellationToken cancellationToken);
}
