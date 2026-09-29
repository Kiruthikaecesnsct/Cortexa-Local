using Cortexa.JobOrchestrator.Infrastructure.Messaging;

namespace Cortexa.JobOrchestrator.Infrastructure.Configuration;

public sealed class ServiceBusSettings
{
    public string NamespaceFqdn { get; set; } = string.Empty;
    public string SubscriptionName { get; set; } = string.Empty;

    public int MaxDeliveryCount { get; set; } = 5;
    public int MaxRetries { get; set; } = 3;
    public int[] RetryBackoffSeconds { get; set; } = [1, 2, 4];

    public int ConcurrencyConflictMaxAttempts { get; set; } = 3;
    public int ConcurrencyConflictBackoffMilliseconds { get; set; } = 150;

    public Dictionary<string, string> TopicNames { get; set; } = [];
    public Dictionary<string, string> WorkerSubscriptions { get; set; } = [];
    public bool UseSessions { get; set; } = true;
    public int MaxConcurrentSessions { get; set; } = 8;
    public bool DrainDeadLetters { get; set; } = true;
    public int CandidateSessionDrainBudgetSeconds { get; set; } = 8;
    public int SessionAcceptTimeoutSeconds { get; set; } = 2;

    public static IReadOnlySet<string> CandidateScopedEventTypes => SessionKeyResolver.CandidateScopedEventTypes;
}
