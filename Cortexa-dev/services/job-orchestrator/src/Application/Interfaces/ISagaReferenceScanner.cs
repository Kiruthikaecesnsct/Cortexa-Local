using Cortexa.JobOrchestrator.Application.Models;

namespace Cortexa.JobOrchestrator.Application.Interfaces;

public interface ISagaReferenceScanner
{
    Task<IReadOnlyList<DanglingReference>> FindDanglingAsync(CancellationToken ct);
}
