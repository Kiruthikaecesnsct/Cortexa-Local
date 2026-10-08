using Collector.Domain.History;
using Collector.Server.Application.Rows;

namespace Collector.Server.Application.Reads;

public static class CandidateAssembler
{
    public static IReadOnlyList<BatchCandidate> Assemble(
        BatchResultRows results,
        IReadOnlyList<VerdictSummaryRow> verdicts,
        IReadOnlyList<EvidenceCountRow> evidenceCounts,
        IReadOnlyList<ChunkKnowledgeRow> chunks)
    {
        var context = new AssemblyContext(
            LatestVerdicts(verdicts),
            SumEvidence(evidenceCounts),
            KnowledgeLinkResolver.IndexChunks(chunks));

        var candidates = new List<BatchCandidate>();
        candidates.AddRange(results.HarvestingCandidates.Select(row => FromHarvesting(row, context)));
        candidates.AddRange((results.SeedingReport?.Opportunities ?? []).Select(row => FromSeeding(row, context)));
        return candidates;
    }

    private static BatchCandidate FromHarvesting(HarvestingReportCandidateRow row, AssemblyContext context) =>
        Build(
            new CandidateHeader(row.CandidateId, RowConstants.HarvestingEngine, row.Title, row.Maturity),
            new ReportScores(row.WeightedScore, row.Axes),
            KnowledgeLinkResolver.ChunkIdsOf(row.ProvenanceLinks),
            context);

    private static BatchCandidate FromSeeding(SeedingOpportunityRow row, AssemblyContext context) =>
        Build(
            new CandidateHeader(row.CandidateId, RowConstants.SeedingEngine, row.Title, row.Category),
            new ReportScores(row.WeightedScore, row.Axes),
            row.GroundedIn?.ChunkIds ?? [],
            context);

    private static BatchCandidate Build(
        CandidateHeader header,
        ReportScores report,
        IEnumerable<string> chunkIds,
        AssemblyContext context)
    {
        context.Verdicts.TryGetValue(header.CandidateId, out var verdict);
        return new BatchCandidate
        {
            CandidateId = header.CandidateId,
            Engine = header.Engine,
            Title = header.Title,
            Kind = header.Kind,
            EvidenceCount = context.EvidenceCounts.GetValueOrDefault(header.CandidateId),
            Score = verdict?.CompositeScore ?? report.WeightedScore,
            Patentability = ToPatentability(verdict?.Patentability ?? report.PatentabilityOrNull()),
            KnowledgeLinks = KnowledgeLinkResolver.Resolve(chunkIds, context.ChunkIndex)
        };
    }

    private static int? ToPatentability(double? score) =>
        score is null ? null : (int)Math.Round(score.Value, MidpointRounding.AwayFromZero);

    private static Dictionary<string, VerdictSummaryRow> LatestVerdicts(IEnumerable<VerdictSummaryRow> verdicts)
    {
        var map = new Dictionary<string, VerdictSummaryRow>(StringComparer.Ordinal);
        foreach (var verdict in verdicts)
        {
            map[verdict.CandidateId] = verdict;
        }

        return map;
    }

    private static Dictionary<string, int> SumEvidence(IEnumerable<EvidenceCountRow> rows)
    {
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            totals[row.CandidateId] = totals.GetValueOrDefault(row.CandidateId) + row.HitCount;
        }

        return totals;
    }

    private sealed record AssemblyContext(
        IReadOnlyDictionary<string, VerdictSummaryRow> Verdicts,
        IReadOnlyDictionary<string, int> EvidenceCounts,
        IReadOnlyDictionary<string, ChunkKnowledgeRow> ChunkIndex);

    private sealed record CandidateHeader(string CandidateId, string Engine, string Title, string Kind);

    private sealed record ReportScores(double? WeightedScore, IReadOnlyDictionary<string, AxisScoreRow> Axes)
    {
        public double? PatentabilityOrNull() =>
            Axes.TryGetValue(RowConstants.PatentabilityAxis, out var axis) ? axis.Score : null;
    }
}
