using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using Cortexa.JobOrchestrator.Domain.Errors;

namespace Cortexa.JobOrchestrator.Domain.StateMachine;

public static class DocumentTransitions
{
    private static readonly IReadOnlyDictionary<DocumentState, IReadOnlySet<DocumentState>> Allowed =
        new Dictionary<DocumentState, IReadOnlySet<DocumentState>>
        {
            [DocumentState.Queued] = new HashSet<DocumentState> { DocumentState.Ingested, DocumentState.Failed, DocumentState.Cancelled },
            [DocumentState.Ingested] = new HashSet<DocumentState> { DocumentState.Extracted, DocumentState.NoCandidates, DocumentState.Failed, DocumentState.Cancelled },
            [DocumentState.Extracted] = new HashSet<DocumentState> { DocumentState.Scored, DocumentState.Complete, DocumentState.Failed, DocumentState.Cancelled },
            [DocumentState.Scored] = new HashSet<DocumentState> { DocumentState.Harvested, DocumentState.Seeded, DocumentState.Complete, DocumentState.Failed, DocumentState.Cancelled },
            [DocumentState.Harvested] = new HashSet<DocumentState> { DocumentState.Seeded, DocumentState.Complete, DocumentState.Failed, DocumentState.Cancelled },
            [DocumentState.Seeded] = new HashSet<DocumentState> { DocumentState.Complete, DocumentState.Failed, DocumentState.Cancelled },
            [DocumentState.Complete] = new HashSet<DocumentState>(),
            [DocumentState.Failed] = new HashSet<DocumentState>(),
            [DocumentState.Cancelled] = new HashSet<DocumentState>(),
            [DocumentState.NoCandidates] = new HashSet<DocumentState>()
        };

    public static bool CanAdvance(DocumentState from, DocumentState to)
    {
        if (!Allowed.TryGetValue(from, out var targets))
            return false;

        return targets.Contains(to);
    }

    public static IReadOnlySet<DocumentState> ValidTransitions(DocumentState from)
    {
        return Allowed.TryGetValue(from, out var targets) ? targets : new HashSet<DocumentState>();
    }

    public static void Advance(DocumentProgress document, DocumentState target)
    {
        if (CanAdvance(document.State, target))
        {
            document.State = target;
            return;
        }

        var validNext = ValidTransitions(document.State).Select(s => s.ToString());

        throw new InvalidTransitionException(
            document.State.ToString(),
            target.ToString(),
            document.DocumentId,
            validNext);
    }
}
