using Collector.Domain.History;
using Collector.Server.Application.Reads;
using Collector.Server.Application.Rows;
using Collector.Server.Tests.Fakes;

namespace Collector.Server.Tests.Reads;

public class CandidateAssemblerTests
{
    private const double VerdictScore = 0.8;
    private const double ReportScore = 0.5;
    private const int VerdictPatentability = 70;
    private const int ReportPatentability = 55;
    private const double FractionalPatentability = 70.6;
    private const int RoundedPatentability = 71;

    [Fact]
    public void Assemble_VerdictPresent_UsesVerdictScoreAndPatentability()
    {
        var rows = Rows(ResultRows.Harvesting("c1", ReportScore, ReportPatentability));

        var candidate = Assert.Single(Assemble(rows, ResultRows.Verdict("c1", VerdictScore, VerdictPatentability)));

        Assert.Equal(VerdictScore, candidate.Score);
        Assert.Equal(VerdictPatentability, candidate.Patentability);
    }

    [Fact]
    public void Assemble_NoVerdict_FallsBackToReportValues()
    {
        var rows = Rows(ResultRows.Harvesting("c1", ReportScore, ReportPatentability));

        var candidate = Assert.Single(Assemble(rows));

        Assert.Equal(ReportScore, candidate.Score);
        Assert.Equal(ReportPatentability, candidate.Patentability);
    }

    [Fact]
    public void Assemble_VerdictWithoutPatentability_FallsBackToReportAxis()
    {
        var rows = Rows(ResultRows.Harvesting("c1", ReportScore, ReportPatentability));

        var candidate = Assert.Single(Assemble(rows, ResultRows.Verdict("c1", VerdictScore)));

        Assert.Equal(VerdictScore, candidate.Score);
        Assert.Equal(ReportPatentability, candidate.Patentability);
    }

    [Fact]
    public void Assemble_NeitherVerdictNorReportScores_ReturnsNulls()
    {
        var candidate = Assert.Single(Assemble(Rows(ResultRows.Harvesting("c1"))));

        Assert.Null(candidate.Score);
        Assert.Null(candidate.Patentability);
    }

    [Fact]
    public void Assemble_RoundsFractionalPatentability()
    {
        var rows = Rows(ResultRows.Harvesting("c1"));

        var candidate = Assert.Single(Assemble(rows, ResultRows.Verdict("c1", VerdictScore, FractionalPatentability)));

        Assert.Equal(RoundedPatentability, candidate.Patentability);
    }

    [Fact]
    public void Assemble_SumsEvidenceAcrossBundlesPerCandidate()
    {
        var rows = Rows(ResultRows.Harvesting("c1"), ResultRows.Harvesting("c2"));
        EvidenceCountRow[] evidence = [ResultRows.Evidence("c1", 2), ResultRows.Evidence("c1", 4), ResultRows.Evidence("c2", 1)];

        var candidates = CandidateAssembler.Assemble(rows, [], evidence, []);

        Assert.Equal(6, candidates.Single(c => c.CandidateId == "c1").EvidenceCount);
        Assert.Equal(1, candidates.Single(c => c.CandidateId == "c2").EvidenceCount);
    }

    [Fact]
    public void Assemble_CandidateWithoutEvidence_HasZeroCount()
    {
        var candidate = Assert.Single(Assemble(Rows(ResultRows.Harvesting("c1"))));

        Assert.Equal(0, candidate.EvidenceCount);
    }

    [Fact]
    public void Assemble_HarvestingCandidate_UsesMaturityAsKindAndEngine()
    {
        var candidate = Assert.Single(Assemble(Rows(ResultRows.Harvesting("c1"))));

        Assert.Equal("harvesting", candidate.Engine);
        Assert.Equal("Mature", candidate.Kind);
        Assert.Equal("Title c1", candidate.Title);
    }

    [Fact]
    public void Assemble_SeedingOpportunity_UsesCategoryAsKindAndEngine()
    {
        var rows = new BatchResultRows([], ResultRows.Report(ResultRows.Opportunity("s1")));

        var candidate = Assert.Single(Assemble(rows));

        Assert.Equal("seeding", candidate.Engine);
        Assert.Equal("adjacent", candidate.Kind);
    }

    [Fact]
    public void Assemble_ResolvesLinksFromProvenanceAndGrounding()
    {
        var chunkOne = ResultRows.Chunk("doc-1", 0);
        var chunkTwo = ResultRows.Chunk("doc-2", 3);
        var rows = new BatchResultRows(
            [ResultRows.Harvesting("c1", links: ResultRows.Link("doc-1", 0))],
            ResultRows.Report(ResultRows.Opportunity("s1", chunkIds: chunkTwo.Id)));

        var candidates = CandidateAssembler.Assemble(rows, [], [], [Knowledge(chunkOne), Knowledge(chunkTwo)]);

        Assert.Equal("doc-1", Assert.Single(candidates[0].KnowledgeLinks).Source.DocumentId);
        Assert.Equal("doc-2", Assert.Single(candidates[1].KnowledgeLinks).Source.DocumentId);
    }

    [Fact]
    public void Assemble_LinkToMissingChunk_IsSkipped()
    {
        var present = ResultRows.Chunk("doc-1", 0);
        var rows = new BatchResultRows(
            [ResultRows.Harvesting("c1", links: [ResultRows.Link("doc-1", 0), ResultRows.Link("doc-gone", 0)])],
            null);

        var candidate = Assert.Single(CandidateAssembler.Assemble(rows, [], [], [Knowledge(present)]));

        Assert.Single(candidate.KnowledgeLinks);
    }

    [Fact]
    public void Assemble_DuplicateChunkReferences_ProduceOneLink()
    {
        var chunk = ResultRows.Chunk("doc-1", 0);
        var rows = new BatchResultRows(
            [ResultRows.Harvesting("c1", links: [ResultRows.Link("doc-1", 0), ResultRows.Link("doc-1", 0)])],
            null);

        var candidate = Assert.Single(CandidateAssembler.Assemble(rows, [], [], [Knowledge(chunk)]));

        Assert.Single(candidate.KnowledgeLinks);
    }

    [Fact]
    public void Assemble_NoReports_ReturnsEmpty()
    {
        Assert.Empty(CandidateAssembler.Assemble(new BatchResultRows([], null), [], [], []));
    }

    private static BatchResultRows Rows(params HarvestingReportCandidateRow[] candidates) => new(candidates, null);

    private static IReadOnlyList<BatchCandidate> Assemble(BatchResultRows rows, params VerdictSummaryRow[] verdicts) =>
        CandidateAssembler.Assemble(rows, verdicts, [], []);

    private static ChunkKnowledgeRow Knowledge(ChunkRow chunk) => new()
    {
        Id = chunk.Id,
        DocumentId = chunk.DocumentId,
        Knowledge = chunk.Knowledge
    };
}
