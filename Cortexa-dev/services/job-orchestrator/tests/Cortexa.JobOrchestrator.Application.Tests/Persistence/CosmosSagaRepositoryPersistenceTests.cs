using System.Text.Json.Serialization;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using Cortexa.JobOrchestrator.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests.Persistence;

public sealed class CosmosSagaRepositoryPersistenceTests
{
    [Fact]
    public void MapToDocument_ThenMapToDomain_PreservesEvidenceTracking()
    {
        var saga = new BatchSaga(
            id: "batch-test",
            state: BatchState.InProgress,
            documents: [],
            wantsHarvesting: false,
            wantsSeeding: false,
            version: 1,
            eTag: null,
            schemaVersion: 1,
            evidenceCompletedCount: 5,
            evidenceSourceLiveCounts: new Dictionary<string, int>
            {
                ["PatentApi"] = 4,
                ["SeedCorpus"] = 3,
                ["LlmResearch"] = 2
            },
            countedEvidenceCandidateIds: ["cand-1", "cand-2", "cand-3", "cand-4", "cand-5"]);

        var mapToDocumentMethod = typeof(CosmosSagaRepository)
            .GetMethod("MapToDocument", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var doc = mapToDocumentMethod.Invoke(null, new object[] { saga }) as SagaDocument;

        var mapToDomainMethod = typeof(CosmosSagaRepository)
            .GetMethod("MapToDomain", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var roundTripped = mapToDomainMethod.Invoke(null, new object?[] { doc!, null }) as BatchSaga;

        roundTripped.Should().NotBeNull();
        roundTripped!.EvidenceCompletedCount.Should().Be(5);
        roundTripped.EvidenceSourceLiveCounts["PatentApi"].Should().Be(4);
        roundTripped.EvidenceSourceLiveCounts["SeedCorpus"].Should().Be(3);
        roundTripped.EvidenceSourceLiveCounts["LlmResearch"].Should().Be(2);
        roundTripped.CountedEvidenceCandidateIds.Should().BeEquivalentTo(
            new[] { "cand-1", "cand-2", "cand-3", "cand-4", "cand-5" });
    }

    [Fact]
    public void MapToDomain_OldDocumentWithoutEvidenceFields_DefaultsToZero()
    {
        var doc = new SagaDocument
        {
            Id = "batch-test",
            BatchId = "batch-test",
            State = "InProgress",
            WantsHarvesting = false,
            WantsSeeding = false,
            Version = 1,
            SchemaVersion = 1,
            Documents = [],
            ActiveDocumentIds = [],
            QueuedDocumentIds = []
        };

        var mapToDomainMethod = typeof(CosmosSagaRepository)
            .GetMethod("MapToDomain", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var saga = mapToDomainMethod.Invoke(null, new object?[] { doc, null }) as BatchSaga;

        saga.Should().NotBeNull();
        saga!.EvidenceCompletedCount.Should().Be(0);
        saga.EvidenceSourceLiveCounts.Should().BeEmpty();
        saga.CountedEvidenceCandidateIds.Should().BeEmpty();
    }
}
