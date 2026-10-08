using System.Collections.Concurrent;
using Collector.Domain.History;
using Collector.Server.Application.Rows;
using Microsoft.Extensions.Logging;

namespace Collector.Server.Application.Reads;

public sealed class BatchStageCalculator(ILogger<BatchStageCalculator> logger)
{
    private readonly ConcurrentDictionary<string, bool> _reportedUnknownStates = new(StringComparer.OrdinalIgnoreCase);

    public BatchStageSummary Calculate(SagaRow saga)
    {
        var ranks = new List<BatchStage>(saga.Documents.Count);
        foreach (var document in saga.Documents)
        {
            var rank = RankOf(document.State, saga.WantsSeeding);
            if (rank is not null)
            {
                ranks.Add(rank.Value);
            }
        }

        return new BatchStageSummary(
            ranks.Count == 0 ? BatchStage.Ingested : ranks.Min(),
            saga.WantsHarvesting ? ranks.Count(rank => rank >= BatchStage.Harvested) : 0,
            saga.WantsHarvesting ? saga.Documents.Count : 0,
            saga.WantsSeeding ? ranks.Count(rank => rank == BatchStage.Seeded) : 0,
            saga.WantsSeeding ? saga.Documents.Count : 0);
    }

    private BatchStage? RankOf(string state, bool wantsSeeding)
    {
        if (Is(state, RowConstants.SagaDocumentStateFailed) || Is(state, RowConstants.SagaDocumentStateCancelled))
        {
            return null;
        }

        if (Is(state, RowConstants.SagaDocumentStateComplete) || Is(state, RowConstants.SagaDocumentStateNoCandidates))
        {
            return wantsSeeding ? BatchStage.Seeded : BatchStage.Harvested;
        }

        return KnownRank(state) ?? ReportUnknown(state);
    }

    private static BatchStage? KnownRank(string state)
    {
        if (Is(state, RowConstants.SagaDocumentStateQueued) || Is(state, RowConstants.SagaDocumentStateIngested))
        {
            return BatchStage.Ingested;
        }

        if (Is(state, RowConstants.SagaDocumentStateExtracted))
        {
            return BatchStage.Extracted;
        }

        if (Is(state, RowConstants.SagaDocumentStateScored))
        {
            return BatchStage.Scored;
        }

        if (Is(state, RowConstants.SagaDocumentStateHarvested))
        {
            return BatchStage.Harvested;
        }

        return Is(state, RowConstants.SagaDocumentStateSeeded) ? BatchStage.Seeded : null;
    }

    private BatchStage ReportUnknown(string state)
    {
        if (_reportedUnknownStates.TryAdd(state ?? string.Empty, true))
        {
            logger.LogWarning("Unknown saga document state {State}; treating it as Ingested.", state);
        }

        return BatchStage.Ingested;
    }

    private static bool Is(string state, string expected) =>
        string.Equals(state, expected, StringComparison.OrdinalIgnoreCase);
}
