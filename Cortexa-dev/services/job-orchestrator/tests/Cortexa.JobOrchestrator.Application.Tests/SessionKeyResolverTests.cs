using System.Text.Json;
using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Infrastructure.Messaging;
using FluentAssertions;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class SessionKeyResolverTests
{
    private const string BatchId = "batch-abc";
    private const string CandidateId = "cand-1";

    public static IEnumerable<object[]> ResolveCases()
    {
        yield return
        [
            SagaEventType.EvidenceRequested,
            new Dictionary<string, object> { [PayloadKeys.CandidateId] = CandidateId },
            $"{BatchId}:{CandidateId}",
            true
        ];
        yield return
        [
            SagaEventType.ScoringRequested,
            new Dictionary<string, object>
            {
                [PayloadKeys.CandidateId] = JsonDocument.Parse($"\"{CandidateId}\"").RootElement
            },
            $"{BatchId}:{CandidateId}",
            true
        ];
        yield return
        [
            SagaEventType.ExtractionRequested,
            new Dictionary<string, object> { [PayloadKeys.CandidateId] = CandidateId },
            BatchId,
            false
        ];
        yield return
        [
            SagaEventType.IngestionRequested,
            new Dictionary<string, object>(),
            BatchId,
            false
        ];
        yield return
        [
            SagaEventType.BatchCreated,
            new Dictionary<string, object>(),
            BatchId,
            false
        ];
        yield return
        [
            SagaEventType.EvidenceRequested,
            new Dictionary<string, object>(),
            BatchId,
            false
        ];
        yield return
        [
            SagaEventType.ScoringRequested,
            new Dictionary<string, object> { [PayloadKeys.CandidateId] = "   " },
            BatchId,
            false
        ];
        yield return
        [
            SagaEventType.EvidenceRequested,
            new Dictionary<string, object> { [PayloadKeys.CandidateId] = string.Empty },
            BatchId,
            false
        ];
    }

    private static EventEnvelope CreateEnvelope(string eventType, Dictionary<string, object> payload) =>
        new()
        {
            EventType = eventType,
            BatchId = BatchId,
            Payload = payload
        };

    [Theory]
    [MemberData(nameof(ResolveCases))]
    public void Resolve_ReturnsExpectedSessionIdAndProperties(
        string eventType,
        Dictionary<string, object> payload,
        string expectedSessionId,
        bool expectCandidateProperty)
    {
        var envelope = CreateEnvelope(eventType, payload);

        var result = SessionKeyResolver.Resolve(envelope);

        result.SessionId.Should().Be(expectedSessionId);
        result.ApplicationProperties.Should().ContainKey(SessionKeyResolver.BatchIdProperty)
            .WhoseValue.Should().Be(BatchId);

        if (expectCandidateProperty)
        {
            result.ApplicationProperties.Should().ContainKey(SessionKeyResolver.CandidateIdProperty)
                .WhoseValue.Should().Be(CandidateId);
            result.ApplicationProperties.Should().HaveCount(2);
        }
        else
        {
            result.ApplicationProperties.Should().NotContainKey(SessionKeyResolver.CandidateIdProperty);
            result.ApplicationProperties.Should().HaveCount(1);
        }
    }

    [Theory]
    [InlineData(SagaEventType.EvidenceRequested, true)]
    [InlineData(SagaEventType.ScoringRequested, true)]
    [InlineData(SagaEventType.ExtractionRequested, false)]
    [InlineData(SagaEventType.IngestionRequested, false)]
    [InlineData(SagaEventType.BatchCreated, false)]
    public void IsCandidateScoped_ReturnsExpectedResult(string eventType, bool expected)
    {
        var result = SessionKeyResolver.IsCandidateScoped(eventType);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(SagaEventType.ExtractionRequested, true)]
    [InlineData(SagaEventType.AssetEmbeddingRequested, true)]
    [InlineData(SagaEventType.AssetEmbeddingCompleted, false)]
    [InlineData(SagaEventType.DigestRequested, true)]
    [InlineData(SagaEventType.DigestCompleted, false)]
    [InlineData(SagaEventType.EvidenceRequested, false)]
    [InlineData(SagaEventType.IngestionRequested, false)]
    public void IsUnitScoped_ReturnsExpectedResult(string eventType, bool expected)
    {
        var result = SessionKeyResolver.IsUnitScoped(eventType);

        result.Should().Be(expected);
    }

    [Fact]
    public void Resolve_AssetEmbeddingRequestedWithUnitIndex_ReturnsBatchDocumentUnitScopedSessionId()
    {
        const string DocumentId = "doc-1";
        var payload = new Dictionary<string, object> { [PayloadKeys.UnitIndex] = 2 };
        var envelope = new EventEnvelope
        {
            EventType = SagaEventType.AssetEmbeddingRequested,
            BatchId = BatchId,
            DocumentId = DocumentId,
            Payload = payload
        };

        var result = SessionKeyResolver.Resolve(envelope);

        result.SessionId.Should().Be($"{BatchId}:{DocumentId}:2");
        result.ApplicationProperties.Should().ContainKey(SessionKeyResolver.BatchIdProperty)
            .WhoseValue.Should().Be(BatchId);
    }

    [Fact]
    public void Resolve_DigestRequested_ReturnsDocumentScopedSessionId()
    {
        const string DocumentId = "doc-1";
        var payload = new Dictionary<string, object> { [PayloadKeys.UnitIndex] = 0 };
        var envelope = new EventEnvelope
        {
            EventType = SagaEventType.DigestRequested,
            BatchId = BatchId,
            DocumentId = DocumentId,
            Payload = payload
        };

        var result = SessionKeyResolver.Resolve(envelope);

        result.SessionId.Should().Be($"{BatchId}:{DocumentId}:0");
        result.ApplicationProperties.Should().ContainKey(SessionKeyResolver.BatchIdProperty)
            .WhoseValue.Should().Be(BatchId);
    }

    [Fact]
    public void Resolve_ExtractionRequestedWithUnitIndex_ReturnsBatchDocumentUnitScopedSessionId()
    {
        const string DocumentId = "doc-1";
        var payload = new Dictionary<string, object> { [PayloadKeys.UnitIndex] = 3 };
        var envelope = new EventEnvelope
        {
            EventType = SagaEventType.ExtractionRequested,
            BatchId = BatchId,
            DocumentId = DocumentId,
            Payload = payload
        };

        var result = SessionKeyResolver.Resolve(envelope);

        result.SessionId.Should().Be($"{BatchId}:{DocumentId}:3");
        result.ApplicationProperties.Should().ContainKey(SessionKeyResolver.BatchIdProperty)
            .WhoseValue.Should().Be(BatchId);
        result.ApplicationProperties.Should().NotContainKey(SessionKeyResolver.CandidateIdProperty);
    }

    [Fact]
    public void Resolve_ExtractionRequestedWithJsonElementUnitIndex_ReturnsUnitScopedSessionId()
    {
        const string DocumentId = "doc-1";
        var payload = new Dictionary<string, object>
        {
            [PayloadKeys.UnitIndex] = JsonDocument.Parse("7").RootElement
        };
        var envelope = new EventEnvelope
        {
            EventType = SagaEventType.ExtractionRequested,
            BatchId = BatchId,
            DocumentId = DocumentId,
            Payload = payload
        };

        var result = SessionKeyResolver.Resolve(envelope);

        result.SessionId.Should().Be($"{BatchId}:{DocumentId}:7");
    }

    [Fact]
    public void Resolve_ExtractionRequestedMissingUnitIndex_FallsBackToBatchScoped()
    {
        var envelope = new EventEnvelope
        {
            EventType = SagaEventType.ExtractionRequested,
            BatchId = BatchId,
            DocumentId = "doc-1",
            Payload = new Dictionary<string, object>()
        };

        var result = SessionKeyResolver.Resolve(envelope);

        result.SessionId.Should().Be(BatchId);
        result.ApplicationProperties.Should().HaveCount(1);
    }

    [Fact]
    public void Resolve_ExtractionRequestedWithUnitIndexButNoDocumentId_FallsBackToBatchScoped()
    {
        var envelope = new EventEnvelope
        {
            EventType = SagaEventType.ExtractionRequested,
            BatchId = BatchId,
            DocumentId = null,
            Payload = new Dictionary<string, object> { [PayloadKeys.UnitIndex] = 0 }
        };

        var result = SessionKeyResolver.Resolve(envelope);

        result.SessionId.Should().Be(BatchId);
    }
}
