using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Application.Handlers;
using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests.Handlers;

public sealed class AiModelEnvelopeStamperTests
{
    private const string ExtractionModelId = "gpt-5.5-extraction";
    private const string PrimaryEvidenceModelId = "gpt-5.4-evidence";
    private const string ScoringModelId = "gpt-5.5-scoring";
    private const string SeedingModelId = "gpt-5.5-seeding";
    private const string LegacyModelId = "gpt-5.5-legacy";
    private const string DefaultExtractionModelId = "gpt-5.5";
    private const string DefaultPrimaryEvidenceModelId = "gpt-5.4";
    private const string DefaultScoringModelId = "gpt-5.5";
    private const string DefaultSeedingModelId = "gpt-5.5";

    [Fact]
    public void Stamp_ExtractionRequested_StampsExtractionModel()
    {
        var saga = BuildSaga(extractionModel: ExtractionModelId);
        var envelope = BuildEnvelope(SagaEventType.ExtractionRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(ExtractionModelId);
        envelope.Payload.Should().NotContainKey("secondary_model");
    }

    [Fact]
    public void Stamp_EvidenceRequested_StampsPrimaryModelOnly()
    {
        var saga = BuildSaga(primaryEvidenceModel: PrimaryEvidenceModelId);
        var envelope = BuildEnvelope(SagaEventType.EvidenceRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(PrimaryEvidenceModelId);
        envelope.Payload.Should().NotContainKey("secondary_model");
    }

    [Fact]
    public void Stamp_ScoringRequested_StampsScoringModel()
    {
        var saga = BuildSaga(scoringModel: ScoringModelId);
        var envelope = BuildEnvelope(SagaEventType.ScoringRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(ScoringModelId);
        envelope.Payload.Should().NotContainKey("secondary_model");
    }

    [Fact]
    public void Stamp_IngestionRequested_DoesNotStampAnyModel()
    {
        var saga = BuildSaga(extractionModel: ExtractionModelId);
        var envelope = BuildEnvelope(SagaEventType.IngestionRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().NotContainKey(PayloadKeys.AiModel);
        envelope.Payload.Should().NotContainKey("secondary_model");
    }

    [Fact]
    public void Stamp_ExtractionWithNullPerStageField_FallsBackToLegacyAiModel()
    {
        var saga = BuildSaga(legacyAiModel: LegacyModelId);
        var envelope = BuildEnvelope(SagaEventType.ExtractionRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(LegacyModelId);
    }

    [Fact]
    public void Stamp_EvidenceWithNullPerStageField_FallsBackToLegacyAiModel()
    {
        var saga = BuildSaga(legacyAiModel: LegacyModelId);
        var envelope = BuildEnvelope(SagaEventType.EvidenceRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(LegacyModelId);
    }

    [Fact]
    public void Stamp_ScoringWithNullPerStageField_FallsBackToLegacyAiModel()
    {
        var saga = BuildSaga(legacyAiModel: LegacyModelId);
        var envelope = BuildEnvelope(SagaEventType.ScoringRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(LegacyModelId);
    }

    [Fact]
    public void Stamp_ExtractionWithNullPerStageAndLegacy_FallsBackToDefault()
    {
        var saga = BuildSaga();
        var envelope = BuildEnvelope(SagaEventType.ExtractionRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(DefaultExtractionModelId);
    }

    [Fact]
    public void Stamp_ScoringWithNullPerStageAndLegacy_FallsBackToDefault()
    {
        var saga = BuildSaga();
        var envelope = BuildEnvelope(SagaEventType.ScoringRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(DefaultScoringModelId);
    }

    [Fact]
    public void Stamp_EvidenceWithNullPerStageAndLegacy_FallsBackToGpt54()
    {
        var saga = BuildSaga();
        var envelope = BuildEnvelope(SagaEventType.EvidenceRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(DefaultPrimaryEvidenceModelId);
    }

    [Fact]
    public void StampAll_MultipleEnvelopes_StampsEachCorrectly()
    {
        var saga = BuildSaga(
            extractionModel: ExtractionModelId,
            primaryEvidenceModel: PrimaryEvidenceModelId,
            scoringModel: ScoringModelId);

        var envelopes = new[]
        {
            BuildEnvelope(SagaEventType.IngestionRequested),
            BuildEnvelope(SagaEventType.ExtractionRequested),
            BuildEnvelope(SagaEventType.EvidenceRequested),
            BuildEnvelope(SagaEventType.ScoringRequested)
        };

        AiModelEnvelopeStamper.StampAll(envelopes, saga);

        envelopes[0].Payload.Should().NotContainKey(PayloadKeys.AiModel);

        envelopes[1].Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(ExtractionModelId);

        envelopes[2].Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(PrimaryEvidenceModelId);
        envelopes[2].Payload.Should().NotContainKey("secondary_model");

        envelopes[3].Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(ScoringModelId);
    }

    [Fact]
    public void Stamp_PerStageFieldOverridesLegacyField()
    {
        var saga = BuildSaga(
            extractionModel: ExtractionModelId,
            legacyAiModel: LegacyModelId);
        var envelope = BuildEnvelope(SagaEventType.ExtractionRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(ExtractionModelId);
    }

    [Fact]
    public void Stamp_SeedingRequested_StampsSeedingModel()
    {
        var saga = BuildSaga(seedingModel: SeedingModelId);
        var envelope = BuildEnvelope(SagaEventType.SeedingRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(SeedingModelId);
    }

    [Fact]
    public void Stamp_SeedingWithNullPerStageField_FallsBackToLegacyAiModel()
    {
        var saga = BuildSaga(legacyAiModel: LegacyModelId);
        var envelope = BuildEnvelope(SagaEventType.SeedingRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(LegacyModelId);
    }

    [Fact]
    public void Stamp_SeedingWithNullPerStageAndLegacy_FallsBackToDefault()
    {
        var saga = BuildSaga();
        var envelope = BuildEnvelope(SagaEventType.SeedingRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(DefaultSeedingModelId);
    }

    [Fact]
    public void Stamp_SeedingReportRequested_StampsSeedingModel()
    {
        var saga = BuildSaga(seedingModel: SeedingModelId);
        var envelope = BuildEnvelope(SagaEventType.SeedingReportRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(SeedingModelId);
    }

    [Fact]
    public void Stamp_SeedingReportWithNullSeedingModel_FallsBackToLegacyAiModel()
    {
        var saga = BuildSaga(legacyAiModel: LegacyModelId);
        var envelope = BuildEnvelope(SagaEventType.SeedingReportRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(LegacyModelId);
    }

    [Fact]
    public void Stamp_SeedingReportWithNullSeedingAndLegacy_FallsBackToDefault()
    {
        var saga = BuildSaga();
        var envelope = BuildEnvelope(SagaEventType.SeedingReportRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(DefaultSeedingModelId);
    }

    [Fact]
    public void Stamp_DigestRequested_StampsSeedingModel()
    {
        var saga = BuildSaga(seedingModel: SeedingModelId);
        var envelope = BuildEnvelope(SagaEventType.DigestRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(SeedingModelId);
    }

    [Fact]
    public void Stamp_DigestWithNullSeedingModel_FallsBackToLegacyAiModel()
    {
        var saga = BuildSaga(legacyAiModel: LegacyModelId);
        var envelope = BuildEnvelope(SagaEventType.DigestRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(LegacyModelId);
    }

    [Fact]
    public void Stamp_DigestWithNullSeedingAndLegacy_FallsBackToDefault()
    {
        var saga = BuildSaga();
        var envelope = BuildEnvelope(SagaEventType.DigestRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().ContainKey(PayloadKeys.AiModel)
            .WhoseValue.Should().Be(DefaultSeedingModelId);
    }

    [Fact]
    public void Stamp_DigestCompleted_DoesNotStampAnyModel()
    {
        var saga = BuildSaga(seedingModel: SeedingModelId);
        var envelope = BuildEnvelope(SagaEventType.DigestCompleted);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().NotContainKey(PayloadKeys.AiModel);
    }

    [Fact]
    public void Stamp_HarvestingRequested_DoesNotStampAnyModel()
    {
        var saga = BuildSaga(seedingModel: SeedingModelId);
        var envelope = BuildEnvelope(SagaEventType.HarvestingRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().NotContainKey(PayloadKeys.AiModel);
    }

    [Fact]
    public void Stamp_AssetEmbeddingRequested_DoesNotStampAnyModel()
    {
        var saga = BuildSaga(extractionModel: ExtractionModelId, seedingModel: SeedingModelId);
        var envelope = BuildEnvelope(SagaEventType.AssetEmbeddingRequested);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().NotContainKey(PayloadKeys.AiModel);
    }

    [Fact]
    public void Stamp_AssetEmbeddingCompleted_DoesNotStampAnyModel()
    {
        var saga = BuildSaga(extractionModel: ExtractionModelId, seedingModel: SeedingModelId);
        var envelope = BuildEnvelope(SagaEventType.AssetEmbeddingCompleted);

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().NotContainKey(PayloadKeys.AiModel);
    }

    [Fact]
    public void Stamp_UnrecognizedEventType_DoesNotStampAnyModel()
    {
        var saga = BuildSaga(extractionModel: ExtractionModelId);
        var envelope = BuildEnvelope("unknown.event");

        AiModelEnvelopeStamper.Stamp(envelope, saga);

        envelope.Payload.Should().NotContainKey(PayloadKeys.AiModel);
        envelope.Payload.Should().NotContainKey("secondary_model");
    }

    private static BatchSaga BuildSaga(
        string? extractionModel = null,
        string? primaryEvidenceModel = null,
        string? scoringModel = null,
        string? seedingModel = null,
        string? legacyAiModel = null)
    {
        var metadata = new BatchMetadata(
            ExtractionModel: extractionModel,
            PrimaryEvidenceModel: primaryEvidenceModel,
            ScoringModel: scoringModel,
            SeedingModel: seedingModel,
            AiModel: legacyAiModel);

        return new BatchSaga(
            id: "batch-test",
            state: BatchState.Queued,
            documents: [],
            wantsHarvesting: false,
            wantsSeeding: false,
            version: 1,
            eTag: null,
            schemaVersion: 1,
            metadata: metadata);
    }

    private static EventEnvelope BuildEnvelope(string eventType)
    {
        return new EventEnvelope
        {
            EventType = eventType,
            BatchId = "batch-test"
        };
    }
}
