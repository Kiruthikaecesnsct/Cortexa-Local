using Cortexa.JobOrchestrator.Application.Exceptions;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Mapping;
using Cortexa.JobOrchestrator.Application.Models;
using Cortexa.JobOrchestrator.Domain.Enums;

namespace Cortexa.JobOrchestrator.Application.Handlers;

public sealed class CancelBatchHandler
{
    private readonly ISagaRepository _sagas;

    public CancelBatchHandler(ISagaRepository sagas)
    {
        _sagas = sagas;
    }

    public async Task<StopBatchResponse> HandleAsync(string batchId, string? reason, CancellationToken ct)
    {
        var saga = await _sagas.GetAsync(batchId, ct)
            ?? throw new BatchNotFoundException(batchId);

        if (IsTerminal(saga.State))
            return new StopBatchResponse(batchId, StatusMapper.MapBatchStatus(saga));

        saga.MarkCancelled(reason ?? "Cancelled by user", DateTimeOffset.UtcNow);

        try
        {
            await _sagas.UpdateAsync(saga, ct);
        }
        catch (ConcurrencyConflictException)
        {
            saga = await _sagas.GetAsync(batchId, ct)
                ?? throw new BatchNotFoundException(batchId);

            if (IsTerminal(saga.State))
                return new StopBatchResponse(batchId, StatusMapper.MapBatchStatus(saga));

            saga.MarkCancelled(reason ?? "Cancelled by user", DateTimeOffset.UtcNow);
            await _sagas.UpdateAsync(saga, ct);
        }

        return new StopBatchResponse(batchId, StatusMapper.MapBatchStatus(saga));
    }

    private static bool IsTerminal(BatchState state) =>
        state is BatchState.Completed or BatchState.Failed or BatchState.Cancelled;
}
