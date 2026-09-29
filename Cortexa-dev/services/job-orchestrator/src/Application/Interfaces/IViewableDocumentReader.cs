using Cortexa.JobOrchestrator.Application.Models;

namespace Cortexa.JobOrchestrator.Application.Interfaces;

public interface IViewableDocumentReader
{
    Task<ViewableDocumentLookupResult> GetAsync(string batchId, string documentId, CancellationToken ct);
}
