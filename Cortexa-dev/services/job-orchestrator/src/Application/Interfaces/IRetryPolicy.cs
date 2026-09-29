namespace Cortexa.JobOrchestrator.Application.Interfaces;

public interface IRetryPolicy
{
    int MaxRetries { get; }
    bool ShouldRetry(int attempt);
    TimeSpan DelayFor(int attempt);
}
