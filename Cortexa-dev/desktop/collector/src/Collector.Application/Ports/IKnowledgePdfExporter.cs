using Collector.Application.Knowledge;

namespace Collector.Application.Ports;

public interface IKnowledgePdfExporter
{
    Task ExportAsync(KnowledgePdfReport report, string outputPath, CancellationToken cancellationToken);
}
