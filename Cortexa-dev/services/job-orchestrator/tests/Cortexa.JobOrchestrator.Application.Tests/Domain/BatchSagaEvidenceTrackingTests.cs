using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class BatchSagaEvidenceTrackingTests
{
    [Fact]
    public void RecordEvidenceCoverage_AllSourcesLive_IncrementsAllCounters()
    {
        var saga = BuildSaga();

        saga.RecordEvidenceCoverage(hasPatentApi: true, hasCorpus: true, hasLlm: true);

        saga.EvidenceCompletedCount.Should().Be(1);
        saga.EvidenceSourceLiveCounts["PatentApi"].Should().Be(1);
        saga.EvidenceSourceLiveCounts["SeedCorpus"].Should().Be(1);
        saga.EvidenceSourceLiveCounts["LlmResearch"].Should().Be(1);
    }

    [Fact]
    public void RecordEvidenceCoverage_OnlyPatentApiLive_IncrementsOnlyPatentApi()
    {
        var saga = BuildSaga();

        saga.RecordEvidenceCoverage(hasPatentApi: true, hasCorpus: false, hasLlm: false);

        saga.EvidenceCompletedCount.Should().Be(1);
        saga.EvidenceSourceLiveCounts["PatentApi"].Should().Be(1);
        saga.EvidenceSourceLiveCounts.Should().NotContainKey("SeedCorpus");
        saga.EvidenceSourceLiveCounts.Should().NotContainKey("LlmResearch");
    }

    [Fact]
    public void RecordEvidenceCoverage_NoSourcesLive_IncrementsOnlyCompletedCount()
    {
        var saga = BuildSaga();

        saga.RecordEvidenceCoverage(hasPatentApi: false, hasCorpus: false, hasLlm: false);

        saga.EvidenceCompletedCount.Should().Be(1);
        saga.EvidenceSourceLiveCounts.Should().BeEmpty();
    }

    [Fact]
    public void RecordEvidenceCoverage_MultipleCalls_AccumulatesCorrectly()
    {
        var saga = BuildSaga();

        saga.RecordEvidenceCoverage(hasPatentApi: true, hasCorpus: true, hasLlm: true);
        saga.RecordEvidenceCoverage(hasPatentApi: true, hasCorpus: false, hasLlm: true);
        saga.RecordEvidenceCoverage(hasPatentApi: false, hasCorpus: true, hasLlm: false);

        saga.EvidenceCompletedCount.Should().Be(3);
        saga.EvidenceSourceLiveCounts["PatentApi"].Should().Be(2);
        saga.EvidenceSourceLiveCounts["SeedCorpus"].Should().Be(2);
        saga.EvidenceSourceLiveCounts["LlmResearch"].Should().Be(2);
    }

    [Fact]
    public void RecordEvidenceCoverage_MixedCoverage_AccumulatesIndependently()
    {
        var saga = BuildSaga();

        saga.RecordEvidenceCoverage(hasPatentApi: true, hasCorpus: true, hasLlm: false);
        saga.RecordEvidenceCoverage(hasPatentApi: true, hasCorpus: false, hasLlm: false);
        saga.RecordEvidenceCoverage(hasPatentApi: false, hasCorpus: false, hasLlm: false);

        saga.EvidenceCompletedCount.Should().Be(3);
        saga.EvidenceSourceLiveCounts["PatentApi"].Should().Be(2);
        saga.EvidenceSourceLiveCounts["SeedCorpus"].Should().Be(1);
        saga.EvidenceSourceLiveCounts.Should().NotContainKey("LlmResearch");
    }

    private static BatchSaga BuildSaga()
    {
        return new BatchSaga(
            id: "batch-test",
            state: BatchState.InProgress,
            documents: [],
            wantsHarvesting: false,
            wantsSeeding: false,
            version: 1,
            eTag: null,
            schemaVersion: 1);
    }
}
