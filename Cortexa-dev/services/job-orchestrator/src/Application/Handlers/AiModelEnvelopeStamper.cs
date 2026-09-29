using Cortexa.JobOrchestrator.Application.Contracts;
using Cortexa.JobOrchestrator.Domain.Entities;

namespace Cortexa.JobOrchestrator.Application.Handlers;

public static class AiModelEnvelopeStamper
{
    public static void Stamp(EventEnvelope envelope, BatchSaga saga)
    {
        var eventType = envelope.EventType;

        if (eventType.Equals(SagaEventType.ExtractionRequested, StringComparison.OrdinalIgnoreCase))
        {
            StampModel(envelope, ResolveExtractionModel(saga));
        }
        else if (eventType.Equals(SagaEventType.EvidenceRequested, StringComparison.OrdinalIgnoreCase))
        {
            StampModel(envelope, ResolvePrimaryEvidenceModel(saga));
        }
        else if (eventType.Equals(SagaEventType.ScoringRequested, StringComparison.OrdinalIgnoreCase))
        {
            StampModel(envelope, ResolveScoringModel(saga));
        }
        else if (eventType.Equals(SagaEventType.SeedingRequested, StringComparison.OrdinalIgnoreCase))
        {
            StampModel(envelope, ResolveSeedingModel(saga));
        }
        else if (eventType.Equals(SagaEventType.SeedingReportRequested, StringComparison.OrdinalIgnoreCase))
        {
            StampModel(envelope, ResolveSeedingModel(saga));
        }
        else if (eventType.Equals(SagaEventType.DigestRequested, StringComparison.OrdinalIgnoreCase))
        {
            StampModel(envelope, ResolveDigestModel(saga));
        }
    }

    public static void StampAll(IEnumerable<EventEnvelope> envelopes, BatchSaga saga)
    {
        foreach (var envelope in envelopes)
            Stamp(envelope, saga);
    }

    private static void StampModel(EventEnvelope envelope, string? model)
    {
        if (!string.IsNullOrWhiteSpace(model))
            envelope.Payload[PayloadKeys.AiModel] = model;
    }

    private static string? ResolveExtractionModel(BatchSaga saga)
    {
        return saga.Metadata?.ExtractionModel
               ?? saga.Metadata?.AiModel
               ?? "gpt-5.5";
    }

    private static string? ResolvePrimaryEvidenceModel(BatchSaga saga)
    {
        return saga.Metadata?.PrimaryEvidenceModel
               ?? saga.Metadata?.AiModel
               ?? "gpt-5.4";
    }

    private static string? ResolveScoringModel(BatchSaga saga)
    {
        return saga.Metadata?.ScoringModel
               ?? saga.Metadata?.AiModel
               ?? "gpt-5.5";
    }

    private static string? ResolveSeedingModel(BatchSaga saga)
    {
        return saga.Metadata?.SeedingModel
               ?? saga.Metadata?.AiModel
               ?? "gpt-5.5";
    }

    private static string? ResolveDigestModel(BatchSaga saga)
    {
        return saga.Metadata?.SeedingModel
               ?? saga.Metadata?.AiModel
               ?? "gpt-5.5";
    }
}
