using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Mapping;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Domain.Entities;

namespace Cortexa.JobOrchestrator.Application.Handlers;

public sealed class ListBatchesHandler
{
    private readonly ISagaRepository _sagas;

    public ListBatchesHandler(ISagaRepository sagas)
    {
        _sagas = sagas;
    }

    public async Task<IReadOnlyList<BatchSummaryDto>> HandleAsync(CancellationToken ct)
    {
        var sagas = await _sagas.ListAsync(ct);
        return sagas.Select(MapToSummary).ToList();
    }

    private static BatchSummaryDto MapToSummary(BatchSaga saga)
    {
        var totalDocCount = saga.Metadata?.TotalDocumentCount > 0
            ? saga.Metadata.TotalDocumentCount
            : saga.Documents.Count + saga.QueuedDocumentIds.Count;

        return new BatchSummaryDto
        {
            BatchId = saga.Id,
            Name = saga.Metadata?.BatchName,
            Status = StatusMapper.MapBatchStatus(saga),
            DocumentCount = totalDocCount,
            CompletedCount = saga.CompletedCount,
            SeedingMode = saga.Metadata?.SeedingMode,
            CreatedAt = saga.Metadata?.CreatedAt?.ToString("O") ?? string.Empty
        };
    }
}
