namespace Cortexa.JobOrchestrator.Domain.Errors;

public sealed class InvalidTransitionException : Exception
{
    public string FromState { get; }
    public string ToState { get; }
    public string EntityId { get; }

    public InvalidTransitionException(string fromState, string toState, string entityId, IEnumerable<string> validTransitions)
        : base($"Cannot transition entity '{entityId}' from '{fromState}' to '{toState}'. Valid transitions from '{fromState}': [{string.Join(", ", validTransitions)}].")
    {
        FromState = fromState;
        ToState = toState;
        EntityId = entityId;
    }
}
