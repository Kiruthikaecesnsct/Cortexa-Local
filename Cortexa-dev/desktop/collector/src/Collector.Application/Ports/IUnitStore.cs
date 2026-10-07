using Collector.Domain.Extraction;

namespace Collector.Application.Ports;

public interface IUnitStore
{
    Task InsertAsync(string documentId, IReadOnlyList<ExtractionUnit> units, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExtractionUnit>> GetByDocumentIdAsync(string documentId, CancellationToken cancellationToken);
}
