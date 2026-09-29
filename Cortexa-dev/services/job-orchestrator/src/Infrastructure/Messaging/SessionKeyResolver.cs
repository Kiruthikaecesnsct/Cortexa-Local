using System.Text.Json;
using Cortexa.JobOrchestrator.Application.Contracts;

namespace Cortexa.JobOrchestrator.Infrastructure.Messaging;

public sealed record SessionKeyResult(string SessionId, IReadOnlyDictionary<string, object> ApplicationProperties);

public static class SessionKeyResolver
{
    public const string BatchIdProperty = "batch_id";
    public const string CandidateIdProperty = PayloadKeys.CandidateId;

    public static readonly IReadOnlySet<string> CandidateScopedEventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        SagaEventType.EvidenceRequested,
        SagaEventType.ScoringRequested
    };

    public static readonly IReadOnlySet<string> UnitScopedEventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        SagaEventType.ExtractionRequested,
        SagaEventType.AssetEmbeddingRequested,
        SagaEventType.DigestRequested
    };

    public static SessionKeyResult Resolve(EventEnvelope envelope)
    {
        if (IsCandidateScoped(envelope.EventType))
            return ResolveCandidateScoped(envelope);

        if (IsUnitScoped(envelope.EventType))
            return ResolveUnitScoped(envelope);

        return BatchScoped(envelope.BatchId);
    }

    // Candidate- and unit-scoped session ids are "{batchId}:{...}"; the batch id is the first segment.
    public static string BatchIdFromSessionId(string sessionId)
    {
        var separatorIndex = sessionId.IndexOf(':');
        return separatorIndex < 0 ? sessionId : sessionId[..separatorIndex];
    }

    public static bool IsCandidateScoped(string eventType) =>
        CandidateScopedEventTypes.Contains(eventType);

    public static bool IsUnitScoped(string eventType) =>
        UnitScopedEventTypes.Contains(eventType);

    private static SessionKeyResult ResolveCandidateScoped(EventEnvelope envelope)
    {
        var candidateId = TryGetCandidateId(envelope.Payload);

        return candidateId is null
            ? BatchScoped(envelope.BatchId)
            : CandidateScoped(envelope.BatchId, candidateId);
    }

    private static SessionKeyResult ResolveUnitScoped(EventEnvelope envelope)
    {
        var unitIndex = TryGetUnitIndex(envelope.Payload);

        return unitIndex is null || string.IsNullOrWhiteSpace(envelope.DocumentId)
            ? BatchScoped(envelope.BatchId)
            : UnitScoped(envelope.BatchId, envelope.DocumentId, unitIndex.Value);
    }

    private static SessionKeyResult BatchScoped(string batchId) =>
        new(batchId, new Dictionary<string, object> { [BatchIdProperty] = batchId });

    private static SessionKeyResult CandidateScoped(string batchId, string candidateId) =>
        new(
            $"{batchId}:{candidateId}",
            new Dictionary<string, object>
            {
                [BatchIdProperty] = batchId,
                [CandidateIdProperty] = candidateId
            });

    private static SessionKeyResult UnitScoped(string batchId, string documentId, int unitIndex) =>
        new(
            $"{batchId}:{documentId}:{unitIndex}",
            new Dictionary<string, object>
            {
                [BatchIdProperty] = batchId
            });

    private static string? TryGetCandidateId(Dictionary<string, object> payload)
    {
        if (!payload.TryGetValue(PayloadKeys.CandidateId, out var raw))
            return null;

        var value = raw switch
        {
            string s => s,
            JsonElement e when e.ValueKind == JsonValueKind.String => e.GetString(),
            _ => null
        };

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int? TryGetUnitIndex(Dictionary<string, object> payload)
    {
        if (!payload.TryGetValue(PayloadKeys.UnitIndex, out var raw))
            return null;

        return raw switch
        {
            int i => i,
            long l => (int)l,
            JsonElement e when e.ValueKind == JsonValueKind.Number => e.GetInt32(),
            _ => null
        };
    }
}
