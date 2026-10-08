using Collector.Domain.Enums;
using Collector.Domain.Knowledge;
using Collector.Server.Application.Building;
using Collector.Server.Application.Rows;

namespace Collector.Server.Tests.Fakes;

internal static class ResultRows
{
    public const string Batch = "batch-mine";

    public static ChunkRow Chunk(
        string documentId,
        int orderIndex,
        int? pageNumber = 1,
        KnowledgeSource? source = null)
    {
        var resolvedSource = source ?? new KnowledgeSource { PageNumber = pageNumber };
        return new ChunkRow
        {
            Id = ChunkRowBuilder.BuildChunkId(documentId, orderIndex),
            BatchId = Batch,
            DocumentId = documentId,
            Text = "text",
            OrderIndex = orderIndex,
            StartChar = 0,
            EndChar = 4,
            TokenCount = 1,
            PageNumber = resolvedSource.PageNumber,
            Knowledge = new ChunkKnowledge
            {
                Kind = KnowledgeKind.Logic,
                UnitKind = UnitKind.Section,
                Title = "T",
                Summary = "S",
                Source = resolvedSource,
                Provider = CollectorProvider.Claude,
                Model = "m",
                PromptVersion = "v1"
            }
        };
    }

    public static HarvestingReportCandidateRow Harvesting(
        string candidateId,
        double? weightedScore = null,
        double? patentability = null,
        params ProvenanceLinkRow[] links) => new()
        {
            Id = $"{Batch}:{candidateId}",
            BatchId = Batch,
            Engine = RowConstants.HarvestingEngine,
            DocType = "report_candidate",
            CandidateId = candidateId,
            Title = $"Title {candidateId}",
            Maturity = "Mature",
            WeightedScore = weightedScore,
            Axes = Axes(patentability),
            ProvenanceLinks = links
        };

    public static SeedingOpportunityRow Opportunity(
        string candidateId,
        double? weightedScore = null,
        double? patentability = null,
        params string[] chunkIds) => new()
        {
            CandidateId = candidateId,
            Title = $"Title {candidateId}",
            Category = "adjacent",
            WeightedScore = weightedScore,
            Axes = Axes(patentability),
            GroundedIn = new SeedingGroundingRow { ChunkIds = chunkIds }
        };

    public static SeedingReportRow Report(params SeedingOpportunityRow[] opportunities) => new()
    {
        Id = $"{Batch}:seeding",
        BatchId = Batch,
        Engine = RowConstants.SeedingEngine,
        Opportunities = opportunities
    };

    public static ProvenanceLinkRow Link(string documentId, int chunkIndex) => new()
    {
        DocumentId = documentId,
        SourceChunkIndex = chunkIndex
    };

    public static VerdictSummaryRow Verdict(string candidateId, double composite, double? patentability = null) => new()
    {
        CandidateId = candidateId,
        CompositeScore = composite,
        Patentability = patentability
    };

    public static EvidenceCountRow Evidence(string candidateId, int hitCount) => new()
    {
        CandidateId = candidateId,
        HitCount = hitCount
    };

    private static Dictionary<string, AxisScoreRow> Axes(double? patentability) =>
        patentability is null
            ? []
            : new Dictionary<string, AxisScoreRow>
            {
                [RowConstants.PatentabilityAxis] = new AxisScoreRow { Score = patentability.Value }
            };
}
