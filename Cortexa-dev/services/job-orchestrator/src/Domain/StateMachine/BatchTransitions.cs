using Cortexa.JobOrchestrator.Domain.Entities;
using Cortexa.JobOrchestrator.Domain.Enums;
using Cortexa.JobOrchestrator.Domain.Errors;

namespace Cortexa.JobOrchestrator.Domain.StateMachine;

public static class BatchTransitions
{
    private static readonly IReadOnlyDictionary<BatchState, IReadOnlySet<BatchState>> Allowed =
        new Dictionary<BatchState, IReadOnlySet<BatchState>>
        {
            [BatchState.Queued] = new HashSet<BatchState> { BatchState.InProgress, BatchState.Cancelled },
            [BatchState.InProgress] = new HashSet<BatchState> { BatchState.Completed, BatchState.Failed, BatchState.Cancelled },
            [BatchState.Completed] = new HashSet<BatchState>(),
            [BatchState.Failed] = new HashSet<BatchState>(),
            [BatchState.Cancelled] = new HashSet<BatchState>()
        };

    public static bool CanAdvance(BatchState from, BatchState to)
    {
        if (!Allowed.TryGetValue(from, out var targets))
            return false;

        return targets.Contains(to);
    }

    public static BatchState DeriveFromDocuments(
        IReadOnlyCollection<DocumentProgress> documents,
        BatchState currentState = BatchState.Queued)
    {
        if (currentState == BatchState.Cancelled)
            return BatchState.Cancelled;

        if (documents.Count == 0)
            return BatchState.Queued;

        if (documents.All(d => d.State == DocumentState.Queued))
            return BatchState.Queued;

        if (documents.All(d => d.State is DocumentState.Complete or DocumentState.Failed or DocumentState.NoCandidates))
            return DeriveTerminalState(documents);

        return BatchState.InProgress;
    }

    private static BatchState DeriveTerminalState(IReadOnlyCollection<DocumentProgress> docs) =>
        docs.Any(d => d.State is DocumentState.Complete or DocumentState.NoCandidates) ? BatchState.Completed : BatchState.Failed;

    public static void Advance(BatchSaga batch, BatchState target)
    {
        if (CanAdvance(batch.State, target))
        {
            batch.State = target;
            return;
        }

        var validNext = Allowed.TryGetValue(batch.State, out var targets)
            ? targets.Select(s => s.ToString())
            : [];

        throw new InvalidTransitionException(
            batch.State.ToString(),
            target.ToString(),
            batch.Id,
            validNext);
    }
}
