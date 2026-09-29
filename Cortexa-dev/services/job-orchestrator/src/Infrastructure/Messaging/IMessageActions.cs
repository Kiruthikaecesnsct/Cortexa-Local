namespace Cortexa.JobOrchestrator.Infrastructure.Messaging;

public interface IMessageActions
{
    int RetryAttempt { get; }
    string? CorrelationId { get; }
    Task CompleteAsync(CancellationToken ct);
    Task AbandonAsync(CancellationToken ct);
    Task DeadLetterAsync(string reason, string description, CancellationToken ct);
    Task ScheduleRetryAsync(string messageBody, int nextAttempt, TimeSpan delay, CancellationToken ct);
}
