using Cortexa.JobOrchestrator.Application.Mapping;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests.Mapping;

public sealed class StatusMapperTests
{
    [Fact]
    public void ComputeUnavailableSources_AllSourcesLiveForAllCandidates_ReturnsEmpty()
    {
        var saga = BuildSaga();
        saga.RecordEvidenceCoverage(true, true, true);
        saga.RecordEvidenceCoverage(true, true, true);
        saga.RecordEvidenceCoverage(true, true, true);

        var result = StatusMapper.ComputeUnavailableSources(saga, outageThreshold: 0.0);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ComputeUnavailableSources_LlmDeadForAllCandidates_ReturnsLlmResearch()
    {
        var saga = BuildSaga();
        saga.RecordEvidenceCoverage(true, true, false);
        saga.RecordEvidenceCoverage(true, true, false);
        saga.RecordEvidenceCoverage(true, true, false);

        var result = StatusMapper.ComputeUnavailableSources(saga, outageThreshold: 0.0);

        result.Should().ContainSingle("LlmResearch");
    }

    [Fact]
    public void ComputeUnavailableSources_AllSourcesDeadForAllCandidates_ReturnsAllSources()
    {
        var saga = BuildSaga();
        saga.RecordEvidenceCoverage(false, false, false);
        saga.RecordEvidenceCoverage(false, false, false);

        var result = StatusMapper.ComputeUnavailableSources(saga, outageThreshold: 0.0);

        result.Should().BeEquivalentTo(new[] { "PatentApi", "SeedCorpus", "LlmResearch" });
    }

    [Fact]
    public void ComputeUnavailableSources_LlmLiveForOneCandidate_DoesNotFlagLlm()
    {
        var saga = BuildSaga();
        saga.RecordEvidenceCoverage(true, true, false);
        saga.RecordEvidenceCoverage(true, true, true);
        saga.RecordEvidenceCoverage(true, true, false);

        var result = StatusMapper.ComputeUnavailableSources(saga, outageThreshold: 0.0);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ComputeUnavailableSources_ThresholdPointFive_LlmLiveForHalfCandidates_DoesNotFlagLlm()
    {
        var saga = BuildSaga();
        saga.RecordEvidenceCoverage(true, true, true);
        saga.RecordEvidenceCoverage(true, true, false);

        var result = StatusMapper.ComputeUnavailableSources(saga, outageThreshold: 0.5);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ComputeUnavailableSources_ThresholdPointFive_LlmLiveForLessThanHalf_FlagsLlm()
    {
        var saga = BuildSaga();
        saga.RecordEvidenceCoverage(true, true, true);
        saga.RecordEvidenceCoverage(true, true, false);
        saga.RecordEvidenceCoverage(true, true, false);

        var result = StatusMapper.ComputeUnavailableSources(saga, outageThreshold: 0.5);

        result.Should().ContainSingle("LlmResearch");
    }

    [Fact]
    public void ComputeUnavailableSources_NoEvidenceCompleted_ReturnsEmpty()
    {
        var saga = BuildSaga();

        var result = StatusMapper.ComputeUnavailableSources(saga, outageThreshold: 0.0);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ComputeUnavailableSources_MixedSourceOutage_ReturnsOnlyUnavailableSources()
    {
        var saga = BuildSaga();
        saga.RecordEvidenceCoverage(true, false, false);
        saga.RecordEvidenceCoverage(true, false, false);
        saga.RecordEvidenceCoverage(true, false, false);

        var result = StatusMapper.ComputeUnavailableSources(saga, outageThreshold: 0.0);

        result.Should().BeEquivalentTo(new[] { "SeedCorpus", "LlmResearch" });
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
